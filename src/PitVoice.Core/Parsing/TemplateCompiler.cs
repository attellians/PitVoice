using PitVoice.Core.Intents;

namespace PitVoice.Core.Parsing;

/// <summary>
/// Compiles a pattern string such as <c>"залей {amount} {unit?}"</c> into template tokens. Literal words go through
/// the same normalization as input phrases, so "все четыре" becomes a number literal and "литров" a unit literal.
/// </summary>
public sealed class TemplateCompiler
{
    private readonly Normalizer _normalizer;

    public TemplateCompiler(Normalizer normalizer)
    {
        ArgumentNullException.ThrowIfNull(normalizer);
        _normalizer = normalizer;
    }

    /// <summary>
    /// Compiles <paramref name="pattern"/> for <paramref name="intent"/>. Returns <c>null</c> and fills
    /// <paramref name="errors"/> when the pattern is invalid. All problems are reported, not only the first.
    /// </summary>
    public Template? Compile(string pattern, IntentDefinition intent, ICollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(errors);

        var tokens = new List<TemplateToken>();
        var text = new List<string>();
        var sawAmount = false;
        var sawUnit = false;
        var errorsBefore = errors.Count;

        void FlushText()
        {
            if (text.Count == 0)
            {
                return;
            }

            var words = text.Select(w => new RecognizedWord(w)).ToArray();
            foreach (var token in _normalizer.Tokenize(words, removeFillers: false))
            {
                if (token is WordToken word && _normalizer.Rules.Fillers.Contains(word.Text))
                {
                    errors.Add($"word '{word.Text}' is a filler and would be removed from the phrase before matching");
                    continue;
                }

                tokens.Add(token switch
                {
                    NumberToken n => new NumberLiteral(n.Value),
                    UnitToken u => new UnitLiteral(u.Unit),
                    _ => new WordLiteral(token.Text),
                });
            }

            text.Clear();
        }

        foreach (var piece in pattern.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (piece.StartsWith('{') && piece.EndsWith('}') && piece.Length > 2)
            {
                FlushText();
                var name = piece[1..^1];
                switch (name)
                {
                    case "amount":
                        if (!intent.HasSlot(SlotKind.Amount))
                        {
                            errors.Add($"slot {{amount}} is not allowed for intent '{intent.Id}'");
                        }
                        else if (sawAmount)
                        {
                            errors.Add("slot {amount} appears more than once");
                        }

                        sawAmount = true;
                        tokens.Add(AmountSlot.Instance);
                        break;

                    case "unit":
                    case "unit?":
                        if (!intent.HasSlot(SlotKind.Unit))
                        {
                            errors.Add($"slot {{{name}}} is not allowed for intent '{intent.Id}'");
                        }
                        else if (sawUnit)
                        {
                            errors.Add("slot {unit} appears more than once");
                        }

                        sawUnit = true;
                        tokens.Add(name == "unit" ? UnitSlot.Required : UnitSlot.OptionalSlot);
                        break;

                    default:
                        errors.Add($"unknown slot {{{name}}}; allowed slots: {{amount}}, {{unit}}, {{unit?}}");
                        break;
                }
            }
            else if (piece.Contains('{') || piece.Contains('}'))
            {
                errors.Add($"malformed slot '{piece}'; slots are written as {{amount}}, {{unit}} or {{unit?}}");
            }
            else
            {
                text.Add(piece);
            }
        }

        FlushText();

        if (tokens.Count == 0)
        {
            errors.Add("pattern is empty after normalization");
        }

        if (intent.HasSlot(SlotKind.Amount) && !sawAmount)
        {
            errors.Add($"pattern for intent '{intent.Id}' must contain {{amount}}");
        }

        return errors.Count == errorsBefore ? new Template(intent.Id, pattern, tokens) : null;
    }
}
