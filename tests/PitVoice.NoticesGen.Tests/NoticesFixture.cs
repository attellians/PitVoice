using System.Text.Json;

namespace PitVoice.NoticesGen.Tests;

/// <summary>
/// A temporary restore of a project "PitVoice": a NuGet packages folder with <c>.nuspec</c> files, the
/// <c>project.assets.json</c> that lists them, <c>components.json</c> and a model manifest. No network, no <c>dotnet</c>.
/// </summary>
internal sealed class NoticesFixture : IDisposable
{
    private readonly List<(string Id, string Version, string[] Dependencies)> _packages = [];
    private readonly List<string> _direct = [];
    private readonly List<object> _components = [];

    public NoticesFixture()
    {
        Directory.CreateDirectory(PackagesFolder);
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "pitvoice-notices-" + Guid.NewGuid().ToString("N"));

    private string PackagesFolder => Path.Combine(Root, "packages");

    /// <summary>Adds a package; <paramref name="license"/> null writes a license file instead of an expression.</summary>
    public NoticesFixture Package(string id, string version, string? license, bool direct = true, params string[] dependencies)
    {
        var folder = Path.Combine(PackagesFolder, id.ToLowerInvariant(), version);
        Directory.CreateDirectory(folder);
        var licenseElement = license is null
            ? "<license type=\"file\">LICENSE.txt</license>"
            : $"<license type=\"expression\">{license}</license>";
        File.WriteAllText(Path.Combine(folder, id.ToLowerInvariant() + ".nuspec"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                {licenseElement}
                <projectUrl>https://example.org/{id}</projectUrl>
                <copyright>Copyright {id} authors</copyright>
              </metadata>
            </package>
            """);
        _packages.Add((id, version, dependencies));
        if (direct)
        {
            _direct.Add(id);
        }

        return this;
    }

    public NoticesFixture Component(string name, string version, string license, string? note = null)
    {
        _components.Add(new { name, version, license, url = $"https://example.org/{name}", copyright = (string?)null, note });
        return this;
    }

    public NoticesInput Input()
    {
        var targets = _packages.ToDictionary(
            p => $"{p.Id}/{p.Version}",
            p => (object)new
            {
                type = "package",
                dependencies = p.Dependencies.ToDictionary(d => d, d => _packages.First(x => x.Id == d).Version),
            });
        var libraries = _packages.ToDictionary(
            p => $"{p.Id}/{p.Version}",
            p => (object)new { type = "package", path = $"{p.Id.ToLowerInvariant()}/{p.Version}", files = new[] { $"{p.Id.ToLowerInvariant()}.nuspec" } });
        var assets = new Dictionary<string, object>
        {
            ["version"] = 3,
            ["targets"] = new Dictionary<string, object> { ["net10.0"] = targets },
            ["libraries"] = libraries,
            ["packageFolders"] = new Dictionary<string, object> { [PackagesFolder + Path.DirectorySeparatorChar] = new { } },
            ["project"] = new
            {
                restore = new { projectName = "PitVoice" },
                frameworks = new Dictionary<string, object>
                {
                    ["net10.0"] = new { dependencies = _direct.ToDictionary(d => d, _ => new { target = "Package" }) },
                },
            },
        };
        var assetsPath = Path.Combine(Root, "project.assets.json");
        File.WriteAllText(assetsPath, JsonSerializer.Serialize(assets));

        var componentsPath = Path.Combine(Root, "components.json");
        File.WriteAllText(componentsPath, JsonSerializer.Serialize(new { components = _components, packages = new { } }));

        var manifestPath = Path.Combine(Root, "manifest.json");
        File.WriteAllText(manifestPath, """
            { "models": [ { "id": "vosk-model-small-ru-0.22", "type": "asr", "language": "ru", "license": "Apache-2.0",
              "source": "https://alphacephei.com/vosk/models" } ] }
            """);

        return new NoticesInput([assetsPath], componentsPath, manifestPath, Path.Combine(AppContext.BaseDirectory, "licenses"));
    }

    public NoticesOutput Generate() => Generator.Generate(Input());

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
