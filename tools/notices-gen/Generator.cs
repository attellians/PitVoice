using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PitVoice.NoticesGen;

/// <summary>Generation failed; every problem is one line, the files stay as they were.</summary>
internal sealed class NoticesException : Exception
{
    public NoticesException(string problem)
        : this([problem])
    {
    }

    public NoticesException(IReadOnlyList<string> problems)
        : base(string.Join(Environment.NewLine, problems))
    {
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }
}

/// <summary>A shipped third-party component as the notices file and "About" list it.</summary>
/// <param name="Name">Package id or the name of a native component.</param>
/// <param name="Version">Version as shipped.</param>
/// <param name="License">SPDX expression.</param>
/// <param name="Url">Project address.</param>
internal sealed record Component(string Name, string Version, string License, string Url)
{
    /// <summary>Copyright line when known.</summary>
    [JsonIgnore]
    public string? Copyright { get; init; }

    /// <summary>Where it comes from in the distribution, for native components.</summary>
    [JsonIgnore]
    public string? Note { get; init; }
}

/// <summary>A speech model of the manifest; models are downloaded on demand and not part of the installer.</summary>
internal sealed record ModelNotice(string Id, string License, string Source);

/// <summary>Where the generator reads from.</summary>
/// <param name="AssetsFiles"><c>obj/project.assets.json</c> of every shipped project, the app first.</param>
/// <param name="ComponentsFile">Hand-written descriptions: native components, the .NET runtime, packages without a license expression.</param>
/// <param name="ManifestFile">The model manifest.</param>
/// <param name="LicensesDirectory">License texts, one <c>&lt;SPDX id&gt;.txt</c> per license or exception.</param>
internal sealed record NoticesInput(IReadOnlyList<string> AssetsFiles, string ComponentsFile, string ManifestFile, string LicensesDirectory);

/// <summary>The generated files.</summary>
/// <param name="Components">Every shipped component, sorted by name.</param>
/// <param name="Markdown"><c>THIRD-PARTY-NOTICES.md</c>.</param>
/// <param name="Json"><c>src/PitVoice.App/ThirdParty.json</c>, the list of "About".</param>
internal sealed record NoticesOutput(IReadOnlyList<Component> Components, string Markdown, string Json);

/// <summary>
/// Builds the notices (design decision 8, third-party-notices spec): packages of the app and the CLI with their
/// transitive dependencies, the hand-described components and the models, each with its license, and the full text of
/// every license at the end. Fails with every problem at once: a package whose license is unknown, a license that may
/// not ship, a license without its text.
/// </summary>
internal static class Generator
{
    public const string Command = "dotnet run --project tools/notices-gen";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static NoticesOutput Generate(NoticesInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var problems = new List<string>();
        var manual = ReadManual(input.ComponentsFile);
        var components = new List<Component>();

        var packages = input.AssetsFiles.SelectMany(Packages.Read)
            .GroupBy(p => (p.Id.ToUpperInvariant(), p.Version))
            .Select(g => g.First());
        foreach (var package in packages)
        {
            var metadata = Packages.ReadNuspec(package.NuspecPath);
            manual.Packages.TryGetValue(package.Id, out var described);
            var license = described?.License ?? metadata.LicenseExpression;
            if (license is null)
            {
                problems.Add($"{package.Id} {package.Version}: license cannot be determined (its .nuspec has no license expression); " +
                    $"describe it under \"packages\" in tools/notices-gen/components.json; comes through {package.Chain}");
                continue;
            }

            if (Check(license, manual: false) is { } problem)
            {
                problems.Add($"{package.Id} {package.Version}: {problem}; comes through {package.Chain}");
                continue;
            }

            components.Add(new Component(
                package.Id,
                package.Version,
                license,
                described?.Url ?? metadata.ProjectUrl ?? $"https://www.nuget.org/packages/{package.Id}/{package.Version}")
            {
                Copyright = described?.Copyright ?? metadata.Copyright,
            });
        }

        foreach (var entry in manual.Components)
        {
            if (Check(entry.License, manual: true) is { } problem)
            {
                problems.Add($"{entry.Name} (components.json): {problem}");
                continue;
            }

            components.Add(new Component(entry.Name, entry.Version, entry.License, entry.Url) { Copyright = entry.Copyright, Note = entry.Note });
        }

        var models = ReadModels(input.ManifestFile);
        foreach (var model in models.Where(m => Check(m.License, manual: false) is not null))
        {
            problems.Add($"model {model.Id}: {Check(model.License, manual: false)}");
        }

        components.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) is var byName and not 0
            ? byName
            : string.CompareOrdinal(a.Version, b.Version));

        var texts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in components.Select(c => c.License).Concat(models.Select(m => m.License))
            .Where(l => Check(l, manual: true) is null)
            .SelectMany(l => LicensePolicy.Ids(l, manual: true))
            .Distinct(StringComparer.Ordinal))
        {
            var file = Path.Combine(input.LicensesDirectory, id + ".txt");
            if (File.Exists(file))
            {
                texts[id] = File.ReadAllText(file).ReplaceLineEndings("\n").Trim();
            }
            else
            {
                problems.Add($"no text for license {id}: add tools/notices-gen/licenses/{id}.txt");
            }
        }

        if (problems.Count > 0)
        {
            throw new NoticesException(problems);
        }

        return new NoticesOutput(components, Markdown(components, models, texts), Json(components));
    }

    /// <summary>What differs between the shipped list and <paramref name="current"/> (the committed <c>ThirdParty.json</c>), one line each.</summary>
    public static IReadOnlyList<string> Differences(IReadOnlyList<Component> generated, string? current)
    {
        Component[] old;
        try
        {
            old = current is null ? [] : JsonSerializer.Deserialize<Component[]>(current, ReadOptions) ?? [];
        }
        catch (JsonException)
        {
            old = [];
        }

        static string Key(Component c) => $"{c.Name} {c.Version} ({c.License})";
        var before = old.Select(Key).ToHashSet(StringComparer.Ordinal);
        var after = generated.Select(Key).ToHashSet(StringComparer.Ordinal);
        return [
            .. after.Where(k => !before.Contains(k)).Select(k => "not listed yet: " + k),
            .. before.Where(k => !after.Contains(k)).Select(k => "listed but no longer shipped: " + k),
        ];
    }

    private static string? Check(string expression, bool manual)
    {
        try
        {
            return LicensePolicy.IsAllowed(expression, manual)
                ? null
                : $"license {expression} may not ship (GPL, LGPL, AGPL and non-commercial licenses are forbidden; allowed: {string.Join(", ", LicensePolicy.Allowed)})";
        }
        catch (FormatException e)
        {
            return e.Message;
        }
    }

    private static ManualFile ReadManual(string path)
    {
        if (!File.Exists(path))
        {
            throw new NoticesException($"{path} not found");
        }

        var file = JsonSerializer.Deserialize<ManualFile>(File.ReadAllText(path), ReadOptions)
            ?? throw new NoticesException($"{path} is empty");
        return file with { Packages = new Dictionary<string, ManualPackage>(file.Packages, StringComparer.OrdinalIgnoreCase) };
    }

    private static List<ModelNotice> ReadModels(string path)
    {
        if (!File.Exists(path))
        {
            throw new NoticesException($"{path} not found");
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return [.. document.RootElement.GetProperty("models").EnumerateArray()
            .Select(m => new ModelNotice(m.GetProperty("id").GetString()!, m.GetProperty("license").GetString()!, m.GetProperty("source").GetString()!))];
    }

    private static string Markdown(IReadOnlyList<Component> components, IReadOnlyList<ModelNotice> models, SortedDictionary<string, string> texts)
    {
        var text = new StringBuilder();
        text.Append("# Third-party notices\n\n");
        text.Append("PitVoice is licensed under the MIT License (see `LICENSE`). It ships the third-party components below, each under its own license.\n\n");
        text.Append($"This file is generated by `{Command}` from the NuGet packages of the app and the CLI, `tools/notices-gen/components.json` and `models/manifest.json`. Do not edit it by hand.\n\n");

        text.Append("## Components\n\n");
        text.Append("| Component | Version | License | Copyright | Project |\n");
        text.Append("|---|---|---|---|---|\n");
        foreach (var c in components)
        {
            text.Append($"| {Cell(c.Name)} | {Cell(c.Version)} | {Cell(c.License)} | {Cell(c.Copyright ?? string.Empty)} | {Cell(c.Url)} |\n");
        }

        var notes = components.Where(c => c.Note is not null).ToArray();
        if (notes.Length > 0)
        {
            text.Append('\n');
            foreach (var c in notes)
            {
                text.Append($"- **{c.Name}**: {c.Note}\n");
            }
        }

        text.Append("\n## Speech models\n\n");
        text.Append("Models are not part of the installer: they are downloaded from their source (or the project's `models` release, a copy) when the user asks for it.\n\n");
        text.Append("| Model | License | Source |\n");
        text.Append("|---|---|---|\n");
        foreach (var m in models)
        {
            text.Append($"| {Cell(m.Id)} | {Cell(m.License)} | {Cell(m.Source)} |\n");
        }

        text.Append("\n## License texts\n");
        foreach (var (id, body) in texts)
        {
            text.Append($"\n### {id}\n\n```text\n{body}\n```\n");
        }

        return text.ToString();
    }

    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");

    private static string Json(IReadOnlyList<Component> components) =>
        JsonSerializer.Serialize(components, WriteOptions).ReplaceLineEndings("\n") + "\n";

    private sealed record ManualFile
    {
        public IReadOnlyList<ManualComponent> Components { get; init; } = [];

        public IReadOnlyDictionary<string, ManualPackage> Packages { get; init; } = new Dictionary<string, ManualPackage>();
    }

    private sealed record ManualComponent(string Name, string Version, string License, string Url, string? Copyright, string? Note);

    private sealed record ManualPackage(string License, string? Url, string? Copyright);
}
