using YamlDotNet.RepresentationModel;

namespace PitVoice.Core.Languages;

/// <summary>
/// Maps document paths (<c>intents/fuel.add/patterns/2</c>) to line numbers so that validation errors can point at
/// the exact place in the file.
/// </summary>
internal sealed class YamlLineIndex
{
    private readonly Dictionary<string, int> _lines = new(StringComparer.Ordinal);

    public static YamlLineIndex Empty { get; } = new();

    public static YamlLineIndex Build(string yaml)
    {
        var index = new YamlLineIndex();
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        if (stream.Documents.Count > 0)
        {
            index.Walk(stream.Documents[0].RootNode, string.Empty);
        }

        return index;
    }

    public int? Get(params string[] segments)
    {
        var path = string.Join('/', segments);
        return _lines.TryGetValue(path, out var line) ? line : null;
    }

    private void Walk(YamlNode node, string path)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var (key, value) in mapping.Children)
                {
                    if (key is not YamlScalarNode { Value: { } keyText })
                    {
                        continue;
                    }

                    var childPath = path.Length == 0 ? keyText : $"{path}/{keyText}";
                    _lines[childPath] = (int)key.Start.Line;
                    Walk(value, childPath);
                }

                break;

            case YamlSequenceNode sequence:
                for (var i = 0; i < sequence.Children.Count; i++)
                {
                    var childPath = $"{path}/{i}";
                    _lines[childPath] = (int)sequence.Children[i].Start.Line;
                    Walk(sequence.Children[i], childPath);
                }

                break;
        }
    }
}
