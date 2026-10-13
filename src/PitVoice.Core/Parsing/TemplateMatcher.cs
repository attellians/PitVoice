namespace PitVoice.Core.Parsing;

/// <summary>One way a template matched a token sequence.</summary>
/// <param name="Template">The template.</param>
/// <param name="Distance">Token-level edit distance: 0 for exact, 1 for one inserted, dropped or replaced word.</param>
/// <param name="Amount">Value captured by <c>{amount}</c>.</param>
/// <param name="Unit">Unit captured by <c>{unit}</c> / <c>{unit?}</c>, <c>null</c> when absent.</param>
public sealed record TemplateMatch(Template Template, int Distance, int? Amount, string? Unit)
{
    /// <summary>1.0 for an exact match, 0.75 for a one-word difference.</summary>
    public double Quality => Distance == 0 ? 1.0 : TemplateMatcher.FuzzyQuality;
}

/// <summary>Result of matching one token sequence against every template of a pack.</summary>
/// <param name="Best">The winning match, or <c>null</c> when nothing matched or the best candidates were ambiguous.</param>
/// <param name="IsAmbiguous">True when several intents matched equally well; the phrase is then rejected.</param>
/// <param name="Candidates">All matches, best first.</param>
public sealed record MatchResult(TemplateMatch? Best, bool IsAmbiguous, IReadOnlyList<TemplateMatch> Candidates)
{
    public static MatchResult None { get; } = new(null, false, []);
}

/// <summary>
/// Matches token sequences against templates using edit distance over tokens. Slots bind only to tokens of their
/// type and are never part of an edit. Fuzzy matching (distance 1) is allowed only for templates of three or more
/// tokens and never drops or replaces a negation word, in the phrase or in the template.
/// </summary>
public sealed class TemplateMatcher
{
    public const int MinFuzzyTemplateLength = 3;
    public const int MaxDistance = 1;
    public const double FuzzyQuality = 0.75;

    private readonly IReadOnlyList<Template> _templates;
    private readonly IReadOnlySet<string> _negations;

    public TemplateMatcher(IReadOnlyList<Template> templates, IReadOnlySet<string> negations)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(negations);
        _templates = templates;
        _negations = negations;
    }

    public MatchResult Match(IReadOnlyList<Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
        {
            return MatchResult.None;
        }

        var phrase = tokens.ToArray();
        var candidates = new List<TemplateMatch>();
        foreach (var template in _templates)
        {
            var match = MatchOne(template, phrase);
            if (match is not null)
            {
                candidates.Add(match);
            }
        }

        if (candidates.Count == 0)
        {
            return MatchResult.None;
        }

        candidates.Sort(CompareCandidates);
        var best = candidates[0];
        var ambiguous = candidates.Count > 1
            && candidates[1].Distance == best.Distance
            && candidates[1].Template.Length == best.Template.Length
            && candidates[1].Template.Intent != best.Template.Intent;

        return new MatchResult(ambiguous ? null : best, ambiguous, candidates);
    }

    private static int CompareCandidates(TemplateMatch a, TemplateMatch b)
    {
        var byDistance = a.Distance.CompareTo(b.Distance);
        if (byDistance != 0)
        {
            return byDistance;
        }

        var byLength = b.Template.Length.CompareTo(a.Template.Length); // longer template first
        if (byLength != 0)
        {
            return byLength;
        }

        var byIntent = a.Template.Intent.CompareTo(b.Template.Intent);
        return byIntent != 0 ? byIntent : string.CompareOrdinal(a.Template.Source, b.Template.Source);
    }

    private TemplateMatch? MatchOne(Template template, Token[] phrase)
    {
        var tokens = template.Tokens.ToArray();
        if (phrase.Length < template.RequiredLength - MaxDistance || phrase.Length > tokens.Length + MaxDistance)
        {
            return null;
        }

        if (TryExact(tokens, phrase, out var bindings))
        {
            return new TemplateMatch(template, 0, bindings.Amount, bindings.Unit);
        }

        if (tokens.Length < MinFuzzyTemplateLength)
        {
            return null;
        }

        // Drop one phrase word (the phrase has an extra word).
        for (var k = 0; k < phrase.Length; k++)
        {
            if (!IsEditablePhraseToken(phrase[k]))
            {
                continue;
            }

            if (TryExact(tokens, Without(phrase, k), out bindings))
            {
                return new TemplateMatch(template, 1, bindings.Amount, bindings.Unit);
            }
        }

        // Drop one template word (the phrase misses a word).
        for (var m = 0; m < tokens.Length; m++)
        {
            if (!IsEditableTemplateToken(tokens[m]))
            {
                continue;
            }

            if (TryExact(Without(tokens, m), phrase, out bindings))
            {
                return new TemplateMatch(template, 1, bindings.Amount, bindings.Unit);
            }
        }

        // Replace one template word by one phrase word.
        for (var m = 0; m < tokens.Length; m++)
        {
            if (!IsEditableTemplateToken(tokens[m]))
            {
                continue;
            }

            foreach (var phraseToken in phrase)
            {
                if (!IsEditablePhraseToken(phraseToken))
                {
                    continue;
                }

                var replaced = (TemplateToken[])tokens.Clone();
                replaced[m] = new WordLiteral(phraseToken.Text);
                if (TryExact(replaced, phrase, out bindings))
                {
                    return new TemplateMatch(template, 1, bindings.Amount, bindings.Unit);
                }
            }
        }

        return null;
    }

    private bool IsEditablePhraseToken(Token token) => token is WordToken word && !_negations.Contains(word.Text);

    private bool IsEditableTemplateToken(TemplateToken token) => token is WordLiteral word && !_negations.Contains(word.Text);

    private static T[] Without<T>(T[] source, int index)
    {
        var result = new T[source.Length - 1];
        Array.Copy(source, 0, result, 0, index);
        Array.Copy(source, index + 1, result, index, source.Length - index - 1);
        return result;
    }

    private static bool TryExact(ReadOnlySpan<TemplateToken> template, ReadOnlySpan<Token> phrase, out Bindings bindings)
    {
        bindings = default;
        return Align(template, phrase, ref bindings);
    }

    private static bool Align(ReadOnlySpan<TemplateToken> template, ReadOnlySpan<Token> phrase, ref Bindings bindings)
    {
        if (template.IsEmpty)
        {
            return phrase.IsEmpty;
        }

        var head = template[0];
        if (head is UnitSlot { Optional: true })
        {
            if (!phrase.IsEmpty && phrase[0] is UnitToken unit)
            {
                var withUnit = bindings with { Unit = unit.Unit };
                if (Align(template[1..], phrase[1..], ref withUnit))
                {
                    bindings = withUnit;
                    return true;
                }
            }

            return Align(template[1..], phrase, ref bindings);
        }

        if (phrase.IsEmpty)
        {
            return false;
        }

        var token = phrase[0];
        switch (head)
        {
            case WordLiteral word when token is WordToken w && w.Text == word.Text:
                break;
            case NumberLiteral number when token is NumberToken n && n.Value == number.Value:
                break;
            case UnitLiteral unitLiteral when token is UnitToken u && u.Unit == unitLiteral.Unit:
                break;
            case AmountSlot when token is NumberToken n:
                bindings = bindings with { Amount = n.Value };
                break;
            case UnitSlot when token is UnitToken u:
                bindings = bindings with { Unit = u.Unit };
                break;
            default:
                return false;
        }

        return Align(template[1..], phrase[1..], ref bindings);
    }

    private readonly record struct Bindings(int? Amount, string? Unit);
}
