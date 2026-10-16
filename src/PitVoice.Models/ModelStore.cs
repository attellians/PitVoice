using System.Text.Json;

namespace PitVoice.Models;

/// <summary>A model that finished installing (its directory carries the install marker).</summary>
public sealed record InstalledModel(string Id, string Directory, string Sha256, DateTimeOffset InstalledAt);

/// <summary>
/// Per-user model directory: <c>%LocalAppData%\PitVoice\models\&lt;id&gt;\</c>. A model counts as installed only when its
/// directory contains <see cref="MarkerFileName"/>, which is written as the very last install step (FR-MOD-4).
/// </summary>
public sealed class ModelStore
{
    public const string EnvironmentVariable = "PITVOICE_MODELS_DIR";
    public const string MarkerFileName = "installed.json";
    public const string PartialSuffix = ".zip.part";

    /// <summary>Suffix of a model directory being deleted.</summary>
    public const string DeletingSuffix = ".deleting";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <param name="root">Explicit directory; when null, <see cref="EnvironmentVariable"/> and then the default are used.</param>
    public ModelStore(string? root = null)
    {
        Root = Path.GetFullPath(ResolveRoot(root));
    }

    public string Root { get; }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PitVoice", "models");

    public string GetModelDirectory(string id) => Path.Combine(Root, ValidateId(id));

    public string GetPartialFile(string id) => Path.Combine(Root, ValidateId(id) + PartialSuffix);

    public bool IsInstalled(string id) => TryGetInstalled(id, out _);

    public bool TryGetInstalled(string id, out InstalledModel model)
    {
        model = null!;
        var directory = GetModelDirectory(id);
        var marker = ReadMarker(directory);
        if (marker is null || !string.Equals(marker.Id, id, StringComparison.Ordinal))
        {
            return false;
        }

        model = new InstalledModel(id, directory, marker.Sha256 ?? string.Empty, marker.InstalledAt);
        return true;
    }

    /// <summary>Path of an installed model, or null when the model is absent or incomplete.</summary>
    public string? GetInstalledPath(string id) => TryGetInstalled(id, out var model) ? model.Directory : null;

    public IReadOnlyList<InstalledModel> ListInstalled()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        var result = new List<InstalledModel>();
        foreach (var directory in Directory.EnumerateDirectories(Root).Order(StringComparer.Ordinal))
        {
            if (TryGetInstalled(Path.GetFileName(directory), out var model))
            {
                result.Add(model);
            }
        }

        return result;
    }

    /// <summary>Model directories without an install marker (an interrupted extraction); they should be deleted.</summary>
    public IReadOnlyList<string> ListIncomplete()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(Root)
            .Where(d => !File.Exists(Path.Combine(d, MarkerFileName)))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Removes the model directory and any partial download. Returns false when there was nothing to remove. The
    /// directory is first renamed, which Windows refuses as a whole while a file in it is open: a model in use then stays
    /// complete and installed instead of losing some of its files.
    /// </summary>
    public bool Delete(string id)
    {
        var removed = false;
        var directory = GetModelDirectory(id);
        if (Directory.Exists(directory))
        {
            var doomed = directory + DeletingSuffix;
            if (Directory.Exists(doomed))
            {
                Directory.Delete(doomed, recursive: true);
            }

            Directory.Move(directory, doomed);
            Directory.Delete(doomed, recursive: true);
            removed = true;
        }

        var partial = GetPartialFile(id);
        if (File.Exists(partial))
        {
            File.Delete(partial);
            removed = true;
        }

        return removed;
    }

    internal void WriteMarker(string id, string sha256, DateTimeOffset installedAt)
    {
        var marker = new Marker { Id = id, Sha256 = sha256, InstalledAt = installedAt };
        File.WriteAllText(Path.Combine(GetModelDirectory(id), MarkerFileName), JsonSerializer.Serialize(marker, JsonOptions));
    }

    private static Marker? ReadMarker(string directory)
    {
        var path = Path.Combine(directory, MarkerFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Marker>(File.ReadAllText(path));
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }

    private static string ResolveRoot(string? root)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            return root;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? DefaultRoot : fromEnvironment;
    }

    private static string ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id is "." or ".." || id.Contains('/') || id.Contains('\\'))
        {
            throw new ArgumentException($"invalid model id '{id}'", nameof(id));
        }

        return id;
    }

    private sealed class Marker
    {
        public string? Id { get; set; }

        public string? Sha256 { get; set; }

        public DateTimeOffset InstalledAt { get; set; }
    }
}
