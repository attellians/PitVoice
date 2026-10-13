using System.Globalization;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages;

namespace PitVoice.Core.Parsing;

/// <summary>
/// Turns a recognized phrase into commands: normalize → match the whole phrase → otherwise split by conjunctions and
/// match every part → resolve conflicts → apply the confidence threshold. Deterministic; prefers rejection to a wrong
/// command.
/// </summary>
public sealed class CommandParser
{
    private readonly LanguagePack _pack;
    private readonly TemplateMatcher _matcher;

    public CommandParser(LanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        _pack = pack;
        _matcher = new TemplateMatcher(pack.Templates, pack.Negations);
    }

    public LanguagePack Pack => _pack;

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

        var whole = _matcher.Match(tokens);
        List<ParsedCommand> parts;
        if (whole.Best is { Distance: 0 })
        {
            parts = [ToCommand(whole.Best, tokens, options)];
        }
        else
        {
            var pieces = SplitByConjunctions(tokens);
            if (pieces.Count > 1)
            {
                var matched = new List<ParsedCommand>(pieces.Count);
                string? failure = null;
                foreach (var piece in pieces)
                {
                    var match = _matcher.Match(piece);
                    if (match.Best is null)
                    {
                        failure = Describe(piece, match);
                        break;
                    }

                    matched.Add(ToCommand(match.Best, piece, options));
                }

                if (failure is null)
                {
                    parts = matched;
                }
                else if (whole.Best is not null)
                {
                    parts = [ToCommand(whole.Best, tokens, options)];
                }
                else
                {
                    return new ParseResult.Rejected(RejectReason.NotUnderstood, failure);
                }
            }
            else if (whole.Best is not null)
            {
                parts = [ToCommand(whole.Best, tokens, options)];
            }
            else
            {
                return new ParseResult.Rejected(RejectReason.NotUnderstood, Describe(tokens, whole));
            }
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

    private static ParsedCommand ToCommand(TemplateMatch match, IReadOnlyList<Token> tokens, ParseOptions options)
    {
        var definition = IntentCatalog.Get(match.Template.Intent);
        var unit = match.Unit;
        if (unit is null && definition.HasSlot(SlotKind.Unit))
        {
            unit = options.DefaultUnit;
        }

        var recognition = tokens.Count == 0 ? 1.0 : tokens.Min(t => t.Confidence);
        return new ParsedCommand(definition.Id, match.Amount, unit, match.Quality * recognition);
    }

    private static string Describe(IReadOnlyList<Token> tokens, MatchResult match)
    {
        var text = string.Join(' ', tokens.Select(t => t.Text));
        return match.IsAmbiguous
            ? $"'{text}' matches several intents equally well: {string.Join(", ", match.Candidates.Select(c => c.Template.Intent).Distinct())}"
            : $"'{text}' matches no template";
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
}
