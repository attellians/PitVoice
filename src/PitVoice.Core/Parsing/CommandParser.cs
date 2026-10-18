using System.Globalization;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages;

namespace PitVoice.Core.Parsing;

/// <summary>
/// Turns a recognized phrase into commands by word classes (design D2): normalize → split at conjunctions → mark the
/// words of each piece with their classes → split a piece into parts by the actions of different groups → apply the rules
/// of <see cref="WordRules"/> → resolve conflicts → apply the confidence threshold. Word order inside a part is free.
/// Deterministic; prefers rejection to a wrong command.
/// </summary>
public sealed class CommandParser
{
    /// <summary>Match quality of a piece with one word outside the lexicon.</summary>
    public const double FuzzyQuality = 0.75;

    /// <summary>A piece needs this many tokens before one word outside the lexicon is tolerated.</summary>
    public const int MinFuzzyPieceLength = 3;

    private readonly LanguagePack _pack;
    private readonly Dictionary<string, LexiconEntry[]> _entriesByKeys;
    private readonly HashSet<string> _fragmentKeys;

    public CommandParser(LanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        _pack = pack;
        _entriesByKeys = pack.Lexicon.Entries
            .GroupBy(e => string.Join(' ', e.Keys), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        // Words that are only a part of a longer entry: "переднее" of "переднее левое" is not a target by itself.
        _fragmentKeys = pack.Lexicon.Entries.Where(e => e.Keys.Count > 1).SelectMany(e => e.Keys).ToHashSet(StringComparer.Ordinal);
    }

    public LanguagePack Pack => _pack;

    /// <summary>The word a recognized word is read as when it stands alone ("левое" → "левые", design D13).</summary>
    public string ReadAs(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var normalized = Normalizer.NormalizeWord(word);
        return _pack.Normalizer.Rules.WordForms is { } forms && forms.TryGetValue(normalized, out var form) ? form : normalized;
    }

    /// <summary>True for a word that has another form the recognizer may hear instead ("левые", "левое").</summary>
    public bool HasForms(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var normalized = Normalizer.NormalizeWord(word);
        return _pack.Normalizer.Rules.WordForms is { } forms && (forms.ContainsKey(normalized) || forms.Values.Contains(normalized));
    }

    public ParseResult Parse(string text, ParseOptions? options = null) => Parse(RecognizedPhrase.FromText(text), options);

    public ParseResult Parse(RecognizedPhrase phrase, ParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        options ??= ParseOptions.Default;

        if (phrase.Words.Count == 0)
        {
            return new ParseResult.Rejected(RejectReason.Empty, "no words");
        }

        var tokens = _pack.Normalizer.Normalize(phrase);
        if (tokens.Count == 0)
        {
            return new ParseResult.Rejected(RejectReason.NotUnderstood, "nothing left after removing fillers");
        }

        // A reject word means the driver asked for something the pack cannot do ("меняем резину на слики"): the rest of
        // the phrase may look like a command, so nothing of it is taken (design D12).
        if (tokens.FirstOrDefault(t => t is WordToken word && _pack.RejectWords.Contains(word.Text)) is { } reject)
        {
            return new ParseResult.Rejected(RejectReason.NotUnderstood, $"'{reject.Text}' is a reject word: the phrase asks for more than the commands of the pack");
        }

        var parts = new List<ParsedCommand>();
        ParsedCommand? previousTire = null;
        var previousGaveCommands = false;
        foreach (var piece in SplitByConjunctions(tokens))
        {
            var text = string.Join(' ', piece.Select(t => t.Text));
            if (Mark(piece) is not { } spans)
            {
                return new ParseResult.Rejected(RejectReason.NotUnderstood, $"'{text}' can be read as different words");
            }

            if (spans.FirstOrDefault(s => s.Kind == SpanKind.Fragment) is { } fragment)
            {
                return new ParseResult.Rejected(RejectReason.NotUnderstood, $"'{fragment.Text}' in '{text}' is only a part of a longer word class entry");
            }

            var unknown = spans.Count(s => s.Kind == SpanKind.Unknown);
            if (unknown > 1 || (unknown == 1 && piece.Count < MinFuzzyPieceLength))
            {
                return new ParseResult.Rejected(RejectReason.NotUnderstood, $"'{text}' matches no command");
            }

            // "меняй правые меняй левые" without a conjunction is what is left when the recognizer lost "и не": the same
            // command twice in a row would turn a removal into a change, so it is rejected (design D13).
            if (RepeatedAction(spans) is { } repeated)
            {
                return new ParseResult.Rejected(RejectReason.NotUnderstood, $"'{text}' has the action '{repeated.Text}' twice without a conjunction");
            }

            // "левые передние" is the left side and the front axle, or the front left wheel heard in the wrong forms: the
            // recognizer swaps "передние" and "переднее", so it is never guessed (design D13). A wheel is named by its
            // entry ("переднее левое"); a side and an axle need "и" between them.
            if (SideNextToAxle(spans) is { } pair)
            {
                return new ParseResult.Rejected(RejectReason.NotUnderstood, $"'{pair}' in '{text}' is a side and an axle in a row: say «и» between them or name the wheel");
            }

            // A word the parser does not know next to a number may be a part of it ("one thousand" is not 1).
            if (NextToNumber(spans) is { } near)
            {
                return new ParseResult.Rejected(RejectReason.NotUnderstood, $"'{near.Text}' in '{text}' next to a number may be a part of it");
            }

            List<ParsedCommand> commands;
            if (previousGaveCommands && previousTire is not null && TargetsOnly(spans) is { } targets)
            {
                // "меняй левые и передние": the action of the previous tire command carries over (design D2 step 6).
                var remove = IntentCatalog.Get(previousTire.Intent).IsNegation;
                var confidence = MinConfidence(piece);
                commands = targets.Select(t => new ParsedCommand(WordRules.TireIntent(t, remove), null, null, confidence)).ToList();
            }
            else if (SplitIntoParts(spans, options) is { } split)
            {
                commands = split;
            }
            else
            {
                return new ParseResult.Rejected(RejectReason.NotUnderstood, $"'{text}' matches no command or several equally well");
            }

            parts.AddRange(commands);
            previousGaveCommands = commands.Count > 0;
            previousTire = commands.Count > 0 && IntentCatalog.Get(commands[^1].Intent).ExclusionGroup == IntentCatalog.TiresGroup
                ? commands[^1]
                : null;
        }

        var resolution = ConflictResolver.Resolve(parts);
        if (resolution.HasConflict)
        {
            return new ParseResult.Rejected(RejectReason.Conflict, resolution.Conflict!);
        }

        foreach (var part in parts)
        {
            if (part.Confidence < options.ConfidenceThreshold)
            {
                var details = string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{part.Intent}' has confidence {part.Confidence:0.00}, threshold is {options.ConfidenceThreshold:0.00}");
                return new ParseResult.Rejected(RejectReason.LowConfidence, details);
            }
        }

        return new ParseResult.Accepted(resolution.Commands!);
    }

    /// <summary>Splits tokens at conjunctions; multi-word conjunctions are tried before single words.</summary>
    internal IReadOnlyList<IReadOnlyList<Token>> SplitByConjunctions(IReadOnlyList<Token> tokens)
    {
        var parts = new List<IReadOnlyList<Token>>();
        var current = new List<Token>();
        var i = 0;
        while (i < tokens.Count)
        {
            var conjunctionLength = MatchConjunction(tokens, i);
            if (conjunctionLength > 0)
            {
                if (current.Count > 0)
                {
                    parts.Add(current);
                    current = [];
                }

                i += conjunctionLength;
                continue;
            }

            current.Add(tokens[i]);
            i++;
        }

        if (current.Count > 0)
        {
            parts.Add(current);
        }

        return parts;
    }

    private int MatchConjunction(IReadOnlyList<Token> tokens, int start)
    {
        foreach (var conjunction in _pack.Conjunctions)
        {
            if (start + conjunction.Count > tokens.Count)
            {
                continue;
            }

            var matches = true;
            for (var k = 0; k < conjunction.Count; k++)
            {
                if (tokens[start + k] is not WordToken word || word.Text != conjunction[k])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return conjunction.Count;
            }
        }

        return 0;
    }

    // ----- Marking: tokens → spans (design D2 step 3)

    private enum SpanKind
    {
        Entry,
        Number,
        Unit,
        Negation,
        Unknown,
        Fragment,
    }

    /// <summary>A run of tokens read as one thing: a lexicon entry (maybe of several classes), a number, a unit, ...</summary>
    private sealed record Span(SpanKind Kind, IReadOnlyList<Token> Tokens, IReadOnlyList<LexiconEntry> Entries)
    {
        public string Text => string.Join(' ', Tokens.Select(t => t.Text));

        public IEnumerable<WordClass> Classes => Entries.Select(e => e.Class).Distinct();
    }

    /// <summary>
    /// Reads the piece as spans covering every token. Entries of several words are preferred: the reading with the fewest
    /// words outside the lexicon, then the fewest spans, wins. Null when two readings are equally good ("left front right":
    /// "left front" + "right" or "left" + "front right").
    /// </summary>
    private List<Span>? Mark(IReadOnlyList<Token> piece)
    {
        var n = piece.Count;
        var cost = new (int Fallback, int Spans)[n + 1];
        var ways = new int[n + 1];
        var best = new Span?[n + 1];
        var from = new int[n + 1];
        Array.Fill(cost, (int.MaxValue, int.MaxValue));
        cost[0] = (0, 0);
        ways[0] = 1;
        var keys = piece.Select(Lexicon.KeyOf).ToArray();

        for (var start = 0; start < n; start++)
        {
            if (ways[start] == 0)
            {
                continue;
            }

            var options = new List<Span>();
            for (var length = 1; length <= _pack.Lexicon.MaxKeys && start + length <= n; length++)
            {
                if (_entriesByKeys.TryGetValue(string.Join(' ', keys.Skip(start).Take(length)), out var entries))
                {
                    options.Add(new Span(SpanKind.Entry, piece.Skip(start).Take(length).ToArray(), entries));
                }
            }

            // A word alone may be a form the recognizer swaps ("переднее" for "передние", design D13); inside a wheel name
            // the words must be as written, so "передние левое" is not the front left wheel.
            if (!options.Any(o => o.Tokens.Count == 1)
                && _pack.Normalizer.Rules.WordForms is { } forms
                && forms.TryGetValue(keys[start], out var form)
                && _entriesByKeys.TryGetValue(form, out var formEntries))
            {
                options.Add(new Span(SpanKind.Entry, [piece[start]], formEntries));
            }

            if (!options.Any(o => o.Tokens.Count == 1))
            {
                options.Add(Single(piece[start]));
            }

            foreach (var option in options)
            {
                var end = start + option.Tokens.Count;
                var fallback = option.Kind is SpanKind.Unknown or SpanKind.Fragment ? 1 : 0;
                var candidate = (cost[start].Fallback + fallback, cost[start].Spans + 1);
                if (candidate.CompareTo(cost[end]) < 0)
                {
                    cost[end] = candidate;
                    ways[end] = ways[start];
                    best[end] = option;
                    from[end] = start;
                }
                else if (candidate == cost[end])
                {
                    ways[end] = Math.Min(2, ways[end] + ways[start]);
                }
            }
        }

        if (ways[n] != 1)
        {
            return null;
        }

        var spans = new List<Span>();
        for (var end = n; end > 0; end = from[end])
        {
            spans.Add(best[end]!);
        }

        spans.Reverse();
        return spans;
    }

    private Span Single(Token token)
    {
        var kind = token switch
        {
            NumberToken => SpanKind.Number,
            UnitToken => SpanKind.Unit,
            WordToken word when _pack.Negations.Contains(word.Text) => SpanKind.Negation,
            WordToken word when _fragmentKeys.Contains(word.Text) => SpanKind.Fragment,
            _ => SpanKind.Unknown,
        };
        return new Span(kind, [token], []);
    }

    /// <summary>A side target right next to an axle target, each of one word ("левые передние"), or null.</summary>
    private static string? SideNextToAxle(IReadOnlyList<Span> spans)
    {
        static bool Is(Span span, params TireTarget[] targets) =>
            span.Kind == SpanKind.Entry && span.Tokens.Count == 1
            && span.Classes.Any(c => c is { Role: WordRole.Target, Target: { } t } && targets.Contains(t));

        for (var i = 0; i + 1 < spans.Count; i++)
        {
            var (a, b) = (spans[i], spans[i + 1]);
            var side = new[] { TireTarget.Left, TireTarget.Right };
            var axle = new[] { TireTarget.Front, TireTarget.Rear };
            if ((Is(a, side) && Is(b, axle)) || (Is(a, axle) && Is(b, side)))
            {
                return $"{a.Text} {b.Text}";
            }
        }

        return null;
    }

    /// <summary>The second of two actions that can belong to the same group, or null.</summary>
    private static Span? RepeatedAction(IReadOnlyList<Span> spans)
    {
        var seen = new HashSet<LexiconGroup>();
        foreach (var span in spans.Where(s => s.Kind == SpanKind.Entry))
        {
            var groups = span.Classes.Where(c => c.Role == WordRole.Action).Select(c => c.Group).ToArray();
            if (groups.Any(seen.Contains))
            {
                return span;
            }

            seen.UnionWith(groups);
        }

        return null;
    }

    /// <summary>An unknown word right before or after a number, or null.</summary>
    private static Span? NextToNumber(IReadOnlyList<Span> spans)
    {
        for (var i = 0; i < spans.Count; i++)
        {
            if (spans[i].Kind != SpanKind.Unknown)
            {
                continue;
            }

            if ((i > 0 && spans[i - 1].Kind == SpanKind.Number) || (i + 1 < spans.Count && spans[i + 1].Kind == SpanKind.Number))
            {
                return spans[i];
            }
        }

        return null;
    }

    /// <summary>Targets of a piece made only of tire targets (and maybe the tire object), or null.</summary>
    private static List<TireTarget>? TargetsOnly(IReadOnlyList<Span> spans)
    {
        var targets = new List<TireTarget>();
        foreach (var span in spans)
        {
            if (span.Kind != SpanKind.Entry)
            {
                return null;
            }

            var classes = span.Classes.ToArray();
            if (classes.FirstOrDefault(c => c is { Group: LexiconGroup.Tires, Role: WordRole.Target }) is { Target: { } target })
            {
                targets.Add(target);
            }
            else if (!classes.Contains(new WordClass(LexiconGroup.Tires, WordRole.Object)))
            {
                return null;
            }
        }

        return targets.Count > 0 ? targets : null;
    }

    // ----- Parts: spans → commands (design D2 steps 4, 5, 7, 8)

    /// <summary>Commands of one part, compared by value to tell real ambiguity from equal readings.</summary>
    private sealed record PartReading(IReadOnlyList<RuleCommand> Commands, double Quality)
    {
        public bool Equals(PartReading? other) =>
            other is not null && Quality.Equals(other.Quality) && Commands.SequenceEqual(other.Commands);

        public override int GetHashCode() => HashCode.Combine(Commands.Count, Quality);
    }

    /// <summary>A way to read a run of spans: the part readings in order.</summary>
    private sealed record Split(IReadOnlyList<(int Start, int End, PartReading Reading)> Parts)
    {
        public bool SameAs(Split other) =>
            Parts.Count == other.Parts.Count
            && Parts.Zip(other.Parts).All(p => p.First.Reading.Equals(p.Second.Reading));
    }

    /// <summary>
    /// Cuts the spans into consecutive parts that each fit a rule and uses the cut with the fewest parts. When two cuts with
    /// that many parts read differently, or one part reads as different commands, the piece is rejected (null).
    /// </summary>
    private List<ParsedCommand>? SplitIntoParts(IReadOnlyList<Span> spans, ParseOptions options)
    {
        var n = spans.Count;
        var fewest = new int[n + 1];
        var splits = new List<Split>[n + 1];
        Array.Fill(fewest, int.MaxValue);
        fewest[0] = 0;
        splits[0] = [new Split([])];

        for (var end = 1; end <= n; end++)
        {
            for (var start = 0; start < end; start++)
            {
                if (fewest[start] == int.MaxValue)
                {
                    continue;
                }

                var readings = Read(spans, start, end);
                if (readings.Count == 0)
                {
                    continue;
                }

                var count = fewest[start] + 1;
                if (count > fewest[end])
                {
                    continue;
                }

                if (count < fewest[end])
                {
                    fewest[end] = count;
                    splits[end] = [];
                }

                foreach (var before in splits[start])
                {
                    foreach (var reading in readings)
                    {
                        var split = new Split([.. before.Parts, (start, end, reading)]);
                        if (!splits[end].Any(s => s.SameAs(split)) && splits[end].Count < 2)
                        {
                            splits[end].Add(split);
                        }
                    }
                }
            }
        }

        if (fewest[n] == int.MaxValue || splits[n].Count != 1)
        {
            return null;
        }

        var result = new List<ParsedCommand>();
        foreach (var (start, end, reading) in splits[n][0].Parts)
        {
            var confidence = reading.Quality * MinConfidence(spans.Skip(start).Take(end - start).SelectMany(s => s.Tokens).ToArray());
            foreach (var command in reading.Commands)
            {
                var unit = command.Unit;
                if (unit is null && IntentCatalog.Get(command.Intent).HasSlot(SlotKind.Unit))
                {
                    unit = options.DefaultUnit;
                }

                result.Add(new ParsedCommand(command.Intent, command.Amount, unit, confidence));
            }
        }

        return result;
    }

    /// <summary>Every distinct reading of spans [start, end) as one part; at most two are kept.</summary>
    private List<PartReading> Read(IReadOnlyList<Span> spans, int start, int end)
    {
        var part = spans.Skip(start).Take(end - start).ToArray();
        var entrySpans = part.Where(s => s.Kind == SpanKind.Entry).ToArray();
        if (entrySpans.Length == 0)
        {
            return [];
        }

        var quality = part.Any(s => s.Kind == SpanKind.Unknown) ? FuzzyQuality : 1.0;
        var amounts = part.Where(s => s.Kind == SpanKind.Number).Select(s => ((NumberToken)s.Tokens[0]).Value).ToArray();
        var units = part.Where(s => s.Kind == SpanKind.Unit).Select(s => ((UnitToken)s.Tokens[0]).Unit).ToArray();
        var negations = part.Count(s => s.Kind == SpanKind.Negation);

        // Shared words ("смени": tires and windshield) give one reading per choice of class; every class must come from
        // the same group.
        var choices = entrySpans.Select(s => s.Classes.Order().ToArray()).ToArray();
        var readings = new List<PartReading>();
        var chosen = new WordClass[choices.Length];

        void Choose(int index)
        {
            if (readings.Count > 1)
            {
                return;
            }

            if (index == choices.Length)
            {
                var group = chosen[0].Group;
                if (chosen.Any(c => c.Group != group))
                {
                    return;
                }

                var roles = new PartRoles
                {
                    Group = group,
                    Actions = chosen.Count(c => c.Role == WordRole.Action),
                    MultiWordAction = chosen.Select((c, i) => (c, i)).Any(p => p.c.Role == WordRole.Action && entrySpans[p.i].Tokens.Count >= 2),
                    Objects = chosen.Count(c => c.Role == WordRole.Object),
                    Keeps = chosen.Count(c => c.Role == WordRole.Keep),
                    Fasts = chosen.Count(c => c.Role == WordRole.Fast),
                    Targets = chosen.Select((c, i) => (c, i))
                        .Where(p => p.c.Role == WordRole.Target)
                        .Select(p => (p.c.Target!.Value, entrySpans[p.i].Entries.First(e => e.Class == p.c).WordCount))
                        .ToArray(),
                    Negations = negations,
                    Amounts = amounts,
                    Units = units,
                };

                if (WordRules.Apply(roles) is { } commands && commands.All(c => _pack.Supports(c.Intent)))
                {
                    var reading = new PartReading(commands, quality);
                    if (!readings.Contains(reading))
                    {
                        readings.Add(reading);
                    }
                }

                return;
            }

            foreach (var option in choices[index])
            {
                chosen[index] = option;
                Choose(index + 1);
            }
        }

        Choose(0);
        return readings;
    }

    private static double MinConfidence(IReadOnlyList<Token> tokens) => tokens.Count == 0 ? 1.0 : tokens.Min(t => t.Confidence);
}
