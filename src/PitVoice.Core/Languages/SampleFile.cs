using System.Globalization;
using System.Text;
using PitVoice.Core.Parsing;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PitVoice.Core.Languages;

/// <summary>One phrase of a sample list: the recording (none in a script), what is said and the expected result.</summary>
/// <param name="File">Recording next to the list; null in a script of phrases to record.</param>
/// <param name="Say">The phrase as it should be said.</param>
/// <param name="Expect">Expected commands in order, or null when the phrase must be rejected.</param>
/// <param name="Line">1-based line of the entry in its file, when known.</param>
public sealed record SampleEntry(string? File, string Say, IReadOnlyList<ExpectedCommand>? Expect, int? Line = null);

/// <summary>A sample list that cannot be used; the message names the file and the line.</summary>
public sealed class SampleFileException(string message) : Exception(message);

/// <summary>
/// The format of the acceptance sample set (design D9): <c>samples:</c> with <c>file</c>, <c>say</c>, <c>expect</c>, where
/// <c>expect</c> is written like the <c>tests:</c> of a pack. A script of phrases to record (<c>script.&lt;lang&gt;.yaml</c>)
/// has the same format without <c>file</c>; the recording appends one entry per saved take to <c>samples.yaml</c>.
/// </summary>
public static class SampleFile
{
    public const string FileName = "samples.yaml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    /// <param name="path">The list.</param>
    /// <param name="requireFile">True for a recorded set (<c>file</c> is required), false for a script.</param>
    /// <exception cref="SampleFileException">The file is missing or not in the format.</exception>
    public static IReadOnlyList<SampleEntry> Load(string path, bool requireFile)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!System.IO.File.Exists(path))
        {
            throw new SampleFileException($"{path}: file not found");
        }

        var text = System.IO.File.ReadAllText(path);
        SamplesDto? dto;
        YamlLineIndex lines;
        try
        {
            lines = YamlLineIndex.Build(text);
            dto = Deserializer.Deserialize<SamplesDto>(text);
        }
        catch (YamlException e)
        {
            throw new SampleFileException($"{path}:{e.Start.Line}: invalid YAML: {e.Message}");
        }

        var entries = new List<SampleEntry>();
        var items = dto?.Samples ?? [];
        for (var i = 0; i < items.Count; i++)
        {
            var index = i.ToString(CultureInfo.InvariantCulture);
            var line = lines.Get("samples", index);
            var where = line is { } l ? $"{path}:{l}" : path;
            var item = items[i];
            if (item?.Say is not { Length: > 0 } say)
            {
                throw new SampleFileException($"{where}: 'say' is required");
            }

            if (requireFile && item.File is not { Length: > 0 })
            {
                throw new SampleFileException($"{where}: 'file' is required");
            }

            string? problem = null;
            var expect = LanguagePackValidator.ParseExpect(item.Expect, (message, _) => problem ??= message);
            if (expect.Invalid || problem is not null)
            {
                throw new SampleFileException($"{where}: {problem}");
            }

            entries.Add(new SampleEntry(item.File, say.Trim(), expect.Commands, line));
        }

        return entries;
    }

    /// <summary>
    /// Entries that expect an intent the pack cannot give; each problem names the file and the line.
    /// </summary>
    public static IReadOnlyList<string> CheckIntents(IReadOnlyList<SampleEntry> entries, LanguagePack pack, string file)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(pack);
        return entries
            .SelectMany(e => (e.Expect ?? [])
                .Where(c => !pack.Supports(c.Intent))
                .Select(c => $"{(e.Line is { } line ? $"{file}:{line}" : file)}: intent '{c.Intent}' is not in the '{pack.Language}' pack"))
            .ToArray();
    }

    /// <summary>True when <paramref name="actual"/> has the expected intents in order (amount and unit when given).</summary>
    public static bool Matches(IReadOnlyList<ExpectedCommand> expected, IReadOnlyList<ParsedCommand> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return expected.Count == actual.Count && expected.Zip(actual).All(p =>
            p.First.Intent == p.Second.Intent
            && (p.First.Amount is null || p.First.Amount == p.Second.Amount)
            && (p.First.Unit is null || p.First.Unit == p.Second.Unit));
    }

    /// <summary>Appends <paramref name="entry"/> to the list at <paramref name="path"/>, creating it when needed.</summary>
    public static void Append(string path, SampleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entry);
        var existing = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : string.Empty;
        var text = new StringBuilder();
        if (existing.Trim().Length == 0)
        {
            text.Append("samples:\n");
        }
        else if (!existing.EndsWith('\n'))
        {
            text.Append('\n');
        }

        text.Append(Format(entry));
        System.IO.File.AppendAllText(path, text.ToString());
    }

    /// <summary>The entry as YAML lines of a <c>samples:</c> list.</summary>
    public static string Format(SampleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var text = new StringBuilder();
        if (entry.File is { } file)
        {
            text.Append("  - file: ").Append(file).Append('\n');
            text.Append("    say: ").Append(Quote(entry.Say)).Append('\n');
        }
        else
        {
            text.Append("  - say: ").Append(Quote(entry.Say)).Append('\n');
        }

        text.Append("    expect: ").Append(FormatExpect(entry.Expect)).Append('\n');
        return text.ToString();
    }

    public static string FormatExpect(IReadOnlyList<ExpectedCommand>? expect)
    {
        if (expect is null)
        {
            return "reject";
        }

        var commands = expect.Select(c =>
        {
            var parts = new List<string> { $"intent: {c.Intent}" };
            if (c.Amount is { } amount)
            {
                parts.Add($"amount: {amount.ToString(CultureInfo.InvariantCulture)}");
            }

            if (c.Unit is { } unit)
            {
                parts.Add($"unit: {unit}");
            }

            return "{ " + string.Join(", ", parts) + " }";
        });
        return "[" + string.Join(", ", commands) + "]";
    }

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private sealed class SamplesDto
    {
        public List<SampleDto?>? Samples { get; set; }
    }

    private sealed class SampleDto
    {
        public string? File { get; set; }

        public string? Say { get; set; }

        public object? Expect { get; set; }
    }
}
