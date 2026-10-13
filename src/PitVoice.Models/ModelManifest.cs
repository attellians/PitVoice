using System.Text.Json;
using System.Text.Json.Serialization;

namespace PitVoice.Models;

/// <summary>Kind of a downloadable model.</summary>
public enum ModelType
{
    /// <summary>Speech recognition model.</summary>
    Asr,

    /// <summary>Text-to-speech voice.</summary>
    Tts,
}

/// <summary>One entry of <c>models/manifest.json</c> (FR-MOD-1).</summary>
/// <param name="Id">Model id, also the name of its directory in the store.</param>
/// <param name="Type">ASR model or TTS voice.</param>
/// <param name="Language">Language code, e.g. <c>ru</c>.</param>
/// <param name="Url">Primary download URL of the archive.</param>
/// <param name="Mirror">Optional mirror used when the primary source fails.</param>
/// <param name="SizeBytes">Archive size in bytes.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the archive.</param>
/// <param name="License">SPDX license id.</param>
/// <param name="Source">Where the model comes from (project page).</param>
public sealed record ModelEntry(
    string Id,
    ModelType Type,
    string Language,
    Uri Url,
    Uri? Mirror,
    long SizeBytes,
    string Sha256,
    string License,
    string Source);

/// <summary>The manifest could not be read; no model is available for download.</summary>
public sealed class ModelManifestException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>List of models the application knows how to download. Read from disk, never from the network.</summary>
public sealed class ModelManifest
{
    public const string DefaultDirectoryName = "models";
    public const string FileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private ModelManifest(string file, IReadOnlyList<ModelEntry> entries)
    {
        File = file;
        Entries = entries;
    }

    public string File { get; }

    public IReadOnlyList<ModelEntry> Entries { get; }

    public ModelEntry? Find(string id) => Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    /// <summary>Explicit path, else <c>models/manifest.json</c> next to the executable, else in the current directory.</summary>
    public static string ResolvePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var nextToExe = Path.Combine(AppContext.BaseDirectory, DefaultDirectoryName, FileName);
        return System.IO.File.Exists(nextToExe) ? nextToExe : Path.GetFullPath(Path.Combine(DefaultDirectoryName, FileName));
    }

    public static ModelManifest Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string json;
        try
        {
            json = System.IO.File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ModelManifestException($"{path}: cannot read model manifest: {e.Message}", e);
        }

        return Parse(json, path);
    }

    public static ModelManifest Parse(string json, string file)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(file);

        ManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(json, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new ModelManifestException($"{file}: malformed model manifest: {e.Message}", e);
        }

        if (dto?.Models is null)
        {
            throw new ModelManifestException($"{file}: model manifest has no 'models' list");
        }

        var entries = new List<ModelEntry>(dto.Models.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < dto.Models.Count; i++)
        {
            var entry = ToEntry(dto.Models[i], file, i);
            if (!ids.Add(entry.Id))
            {
                throw new ModelManifestException($"{file}: models[{i}] '{entry.Id}': duplicate id");
            }

            entries.Add(entry);
        }

        return new ModelManifest(file, entries);
    }

    private static ModelEntry ToEntry(EntryDto? dto, string file, int index)
    {
        var where = $"{file}: models[{index}]";
        if (dto is null)
        {
            throw new ModelManifestException($"{where}: empty entry");
        }

        var id = Required(dto.Id, "id", where);
        where = $"{where} '{id}'";

        var type = Required(dto.Type, "type", where) switch
        {
            "asr" => ModelType.Asr,
            "tts" => ModelType.Tts,
            var other => throw new ModelManifestException($"{where}: unknown type '{other}' (expected asr or tts)"),
        };

        var sha256 = Required(dto.Sha256, "sha256", where).ToLowerInvariant();
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new ModelManifestException($"{where}: sha256 must be 64 hex characters");
        }

        if (dto.SizeBytes is not > 0)
        {
            throw new ModelManifestException($"{where}: missing or non-positive 'sizeBytes'");
        }

        return new ModelEntry(
            id,
            type,
            Required(dto.Language, "language", where),
            ToUri(Required(dto.Url, "url", where), "url", where),
            string.IsNullOrWhiteSpace(dto.Mirror) ? null : ToUri(dto.Mirror, "mirror", where),
            dto.SizeBytes.Value,
            sha256,
            Required(dto.License, "license", where),
            Required(dto.Source, "source", where));
    }

    private static string Required(string? value, string field, string where) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ModelManifestException($"{where}: missing required field '{field}'")
            : value.Trim();

    private static Uri ToUri(string value, string field, string where) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : throw new ModelManifestException($"{where}: '{field}' is not an http(s) URL: {value}");

    private sealed class ManifestDto
    {
        public List<EntryDto?>? Models { get; set; }
    }

    private sealed class EntryDto
    {
        public string? Id { get; set; }

        public string? Type { get; set; }

        public string? Language { get; set; }

        public string? Url { get; set; }

        public string? Mirror { get; set; }

        [JsonPropertyName("sizeBytes")]
        public long? SizeBytes { get; set; }

        public string? Sha256 { get; set; }

        public string? License { get; set; }

        public string? Source { get; set; }
    }
}
