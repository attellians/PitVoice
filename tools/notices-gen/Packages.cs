using System.Text.Json;
using System.Xml.Linq;

namespace PitVoice.NoticesGen;

/// <summary>A NuGet package that ships with the app or the CLI.</summary>
/// <param name="Id">Package id.</param>
/// <param name="Version">Resolved version.</param>
/// <param name="Chain">How it gets into the distribution, e.g. <c>PitVoice → PitVoice.Host → Serilog.Sinks.File → Serilog</c>.</param>
/// <param name="NuspecPath">Its <c>.nuspec</c> in the NuGet packages folder.</param>
internal sealed record ShippedPackage(string Id, string Version, string Chain, string NuspecPath);

/// <summary>What a <c>.nuspec</c> says about the license.</summary>
/// <param name="LicenseExpression">SPDX expression, or null when the package has none (a license file or only a URL).</param>
/// <param name="ProjectUrl">Project address, or null.</param>
/// <param name="Copyright">Copyright line, or null.</param>
internal sealed record PackageMetadata(string? LicenseExpression, string? ProjectUrl, string? Copyright);

/// <summary>
/// Reads the packages of a project from its <c>obj/project.assets.json</c>, the file restore writes and
/// <c>dotnet list package --include-transitive</c> reads: the resolved packages with their dependencies, so every
/// package gets the chain it comes through (design decision 8). PitVoice's own projects are not listed.
/// </summary>
internal static class Packages
{
    /// <exception cref="NoticesException">The file is missing or has no target.</exception>
    public static IReadOnlyList<ShippedPackage> Read(string assetsPath)
    {
        if (!File.Exists(assetsPath))
        {
            throw new NoticesException($"{assetsPath} not found; run 'dotnet restore PitVoice.sln' first");
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
        var root = document.RootElement;
        var projectName = root.GetProperty("project").GetProperty("restore").GetProperty("projectName").GetString()!;
        var packagesFolder = root.GetProperty("packageFolders").EnumerateObject().First().Name;

        // The target without a runtime id; a restore for win-x64 adds "net10.0-windows/win-x64" with the same packages.
        var target = root.GetProperty("targets").EnumerateObject().FirstOrDefault(t => !t.Name.Contains('/', StringComparison.Ordinal));
        if (target.Value.ValueKind != JsonValueKind.Object)
        {
            throw new NoticesException($"{assetsPath} has no target framework");
        }

        var nodes = new Dictionary<string, (string Version, bool IsPackage, string[] Dependencies)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in target.Value.EnumerateObject())
        {
            var slash = entry.Name.IndexOf('/', StringComparison.Ordinal);
            var dependencies = entry.Value.TryGetProperty("dependencies", out var deps)
                ? deps.EnumerateObject().Select(d => d.Name).ToArray()
                : [];
            nodes[entry.Name[..slash]] = (entry.Name[(slash + 1)..], entry.Value.GetProperty("type").GetString() == "package", dependencies);
        }

        var framework = root.GetProperty("project").GetProperty("frameworks").EnumerateObject().First().Value;
        var direct = framework.TryGetProperty("dependencies", out var directPackages)
            ? directPackages.EnumerateObject().Select(d => d.Name).ToList()
            : [];

        // Projects nobody else depends on are the ones the root references.
        var referenced = nodes.Values.SelectMany(n => n.Dependencies).ToHashSet(StringComparer.OrdinalIgnoreCase);
        direct.AddRange(nodes.Where(n => !n.Value.IsPackage && !referenced.Contains(n.Key)).Select(n => n.Key));

        var previous = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var name in direct.Where(nodes.ContainsKey).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (previous.TryAdd(name, null))
            {
                queue.Enqueue(name);
            }
        }

        while (queue.TryDequeue(out var name))
        {
            foreach (var dependency in nodes[name].Dependencies.Where(nodes.ContainsKey).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (previous.TryAdd(dependency, name))
                {
                    queue.Enqueue(dependency);
                }
            }
        }

        var libraries = root.GetProperty("libraries");
        return nodes
            .Where(n => n.Value.IsPackage)
            .Select(n =>
            {
                var library = libraries.GetProperty($"{n.Key}/{n.Value.Version}");
                var nuspec = library.GetProperty("files").EnumerateArray()
                    .Select(f => f.GetString()!)
                    .FirstOrDefault(f => f.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                    ?? $"{n.Key.ToLowerInvariant()}.nuspec";
                return new ShippedPackage(
                    n.Key,
                    n.Value.Version,
                    Chain(projectName, n.Key, previous),
                    Path.Combine(packagesFolder, library.GetProperty("path").GetString()!, nuspec));
            })
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>License expression, project address and copyright from a <c>.nuspec</c>.</summary>
    public static PackageMetadata ReadNuspec(string path)
    {
        if (!File.Exists(path))
        {
            throw new NoticesException($"{path} not found; run 'dotnet restore PitVoice.sln' first");
        }

        var metadata = XDocument.Load(path).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
        string? Value(string name) => metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;

        var license = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "license");
        var expression = license?.Attribute("type")?.Value == "expression" ? license.Value.Trim() : null;
        return new PackageMetadata(expression, Value("projectUrl"), Value("copyright"));
    }

    private static string Chain(string root, string package, Dictionary<string, string?> previous)
    {
        var path = new List<string>();
        for (string? name = package; name is not null && previous.ContainsKey(name); name = previous[name])
        {
            path.Add(name);
        }

        path.Add(root);
        path.Reverse();
        return string.Join(" → ", path);
    }
}
