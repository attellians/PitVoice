using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using PitVoice.Core.Ptt;

namespace PitVoice.Core.Settings;

/// <summary>
/// Settings in <c>%AppData%\PitVoice\settings.json</c> (design decision 10). Reading is forgiving: a missing file gives
/// defaults, invalid JSON is moved to <c>settings.json.bak</c>, and every missing or invalid field falls back to its
/// default (volume is clamped to 0..100) with a warning. Writing goes to a temporary file that then replaces the old one.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    public const string EnvironmentVariable = "PITVOICE_SETTINGS";
    public const string BackupSuffix = ".bak";

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly string _defaultLanguage;

    /// <param name="path">Explicit file; when null, <see cref="EnvironmentVariable"/> and then the default are used.</param>
    /// <param name="defaultLanguage">Language when the file names none (detected from Windows by the caller).</param>
    public JsonSettingsStore(string? path, string defaultLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLanguage);
        Path = System.IO.Path.GetFullPath(ResolvePath(path));
        _defaultLanguage = defaultLanguage;
    }

    public string Path { get; }

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PitVoice", "settings.json");

    public static string ResolvePath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? DefaultPath : fromEnvironment;
    }

    public SettingsLoadResult Load()
    {
        var defaults = AppSettings.Defaults(_defaultLanguage);
        if (!File.Exists(Path))
        {
            return new SettingsLoadResult(defaults, [], null);
        }

        JsonDocument document;
        try
        {
            // Editors on Windows often save UTF-8 with a byte order mark, which the JSON reader does not accept.
            var bytes = File.ReadAllBytes(Path).AsMemory();
            if (bytes.Span.StartsWith("﻿"u8))
            {
                bytes = bytes[3..];
            }

            document = JsonDocument.Parse(bytes, ReadOptions);
        }
        catch (JsonException e)
        {
            return Recover(defaults, $"settings file {Path} is not valid JSON ({e.Message})");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Recover(defaults, $"settings file {Path} does not contain a JSON object");
            }

            var warnings = new List<string>();
            var settings = Read(document.RootElement, defaults, warnings);
            return new SettingsLoadResult(settings, warnings, null);
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = Path + ".tmp";
        File.WriteAllBytes(temporary, Write(settings));
        if (File.Exists(Path))
        {
            File.Replace(temporary, Path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporary, Path);
        }
    }

    private SettingsLoadResult Recover(AppSettings defaults, string problem)
    {
        var backup = Path + BackupSuffix;
        File.Move(Path, backup, overwrite: true);
        return new SettingsLoadResult(defaults, [$"{problem}; moved to {backup}, using default settings"], backup);
    }

    private static AppSettings Read(JsonElement root, AppSettings defaults, List<string> warnings)
    {
        var settings = defaults;

        if (TryGet(root, "language", out var language))
        {
            if (language.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(language.GetString()))
            {
                settings = settings with { Language = language.GetString()!.Trim() };
            }
            else
            {
                warnings.Add($"settings: invalid language {language.GetRawText()}, using {defaults.Language}");
            }
        }

        if (TryGet(root, "ptt", out var ptt) && ptt.ValueKind == JsonValueKind.Object)
        {
            if (TryGet(ptt, "binding", out var binding) && binding.ValueKind != JsonValueKind.Null)
            {
                settings = settings with { PttBinding = ReadBinding(binding, warnings) };
            }

            if (TryGet(ptt, "mode", out var mode))
            {
                settings = settings with { PttMode = ReadMode(mode, warnings) };
            }
        }

        settings = settings with
        {
            Microphone = ReadDevice(root, "microphone", warnings),
            OutputDevice = ReadDevice(root, "outputDevice", warnings),
        };

        if (TryGet(root, "cueVolume", out var volume))
        {
            if (volume.ValueKind == JsonValueKind.Number && volume.TryGetDouble(out var value))
            {
                var clamped = (int)Math.Round(Math.Clamp(value, 0, AppSettings.MaxCueVolume));
                if (clamped != value)
                {
                    warnings.Add(string.Create(CultureInfo.InvariantCulture, $"settings: cue volume {value} is out of 0..100, using {clamped}"));
                }

                settings = settings with { CueVolume = clamped };
            }
            else
            {
                warnings.Add($"settings: invalid cue volume {volume.GetRawText()}, using {defaults.CueVolume}");
            }
        }

        if (TryGet(root, "defaultUnit", out var unit))
        {
            if (unit.ValueKind == JsonValueKind.String && FuelUnits.IsKnown(unit.GetString()!))
            {
                settings = settings with { DefaultUnit = unit.GetString()! };
            }
            else
            {
                warnings.Add($"settings: unknown default unit {unit.GetRawText()}, using {defaults.DefaultUnit}");
            }
        }

        if (TryGet(root, "threshold", out var threshold))
        {
            if (threshold.ValueKind == JsonValueKind.Number && threshold.TryGetDouble(out var value) && value is >= 0 and <= 1)
            {
                settings = settings with { Threshold = value };
            }
            else
            {
                warnings.Add(string.Create(CultureInfo.InvariantCulture,
                    $"settings: confidence threshold {threshold.GetRawText()} is out of 0..1, using {defaults.Threshold}"));
            }
        }

        if (TryGet(root, "interfaceLanguage", out var interfaceLanguage) && interfaceLanguage.ValueKind != JsonValueKind.Null)
        {
            if (interfaceLanguage.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(interfaceLanguage.GetString()))
            {
                settings = settings with { InterfaceLanguage = interfaceLanguage.GetString()!.Trim() };
            }
            else
            {
                warnings.Add($"settings: invalid interfaceLanguage {interfaceLanguage.GetRawText()}, using the language of the commands");
            }
        }

        if (TryGet(root, "theme", out var theme))
        {
            switch (theme.ValueKind == JsonValueKind.String ? theme.GetString() : null)
            {
                case "system":
                    break;
                case "light":
                    settings = settings with { Theme = AppTheme.Light };
                    break;
                case "dark":
                    settings = settings with { Theme = AppTheme.Dark };
                    break;
                default:
                    warnings.Add($"settings: unknown theme {theme.GetRawText()}, using system");
                    break;
            }
        }

        return settings;
    }

    private static PttBinding? ReadBinding(JsonElement binding, List<string> warnings)
    {
        var kind = binding.ValueKind == JsonValueKind.Object && TryGet(binding, "kind", out var k) && k.ValueKind == JsonValueKind.String
            ? k.GetString()
            : null;
        var name = TryGet(binding, "name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        switch (kind)
        {
            case "joystick"
                when TryGet(binding, "guid", out var guid) && guid.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(guid.GetString())
                    && TryGet(binding, "button", out var button) && button.TryGetInt32(out var number) && number > 0:
                return new PttBinding(PttKey.JoystickButton(guid.GetString()!, number), name ?? "game device");

            case "keyboard"
                when TryGet(binding, "vk", out var vk) && vk.TryGetInt32(out var code) && code is > 0 and < 256:
                return new PttBinding(PttKey.KeyboardKey(code), name ?? string.Create(CultureInfo.InvariantCulture, $"0x{code:X2}"));

            default:
                warnings.Add($"settings: invalid PTT binding {binding.GetRawText()}, PTT is not assigned (run 'ptt bind')");
                return null;
        }
    }

    private static PttMode ReadMode(JsonElement mode, List<string> warnings)
    {
        switch (mode.ValueKind == JsonValueKind.String ? mode.GetString() : null)
        {
            case "hold":
                return PttMode.Hold;
            case "toggle":
                return PttMode.Toggle;
            default:
                warnings.Add($"settings: unknown PTT mode {mode.GetRawText()}, using hold");
                return PttMode.Hold;
        }
    }

    private static string? ReadDevice(JsonElement root, string property, List<string> warnings)
    {
        if (!TryGet(root, property, out var device) || device.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (device.ValueKind == JsonValueKind.String)
        {
            return string.IsNullOrWhiteSpace(device.GetString()) ? null : device.GetString();
        }

        warnings.Add($"settings: invalid {property} {device.GetRawText()}, using the system default");
        return null;
    }

    private static bool TryGet(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out value);
    }

    private static byte[] Write(AppSettings settings)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", AppSettings.CurrentVersion);
            writer.WriteString("language", settings.Language);
            writer.WriteStartObject("ptt");
            if (settings.PttBinding is { } binding)
            {
                writer.WriteStartObject("binding");
                if (binding.Kind == PttDeviceKind.Joystick)
                {
                    writer.WriteString("kind", "joystick");
                    writer.WriteString("guid", binding.Key.Device);
                    writer.WriteNumber("button", binding.Key.Code);
                }
                else
                {
                    writer.WriteString("kind", "keyboard");
                    writer.WriteNumber("vk", binding.Key.Code);
                }

                writer.WriteString("name", binding.Name);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("binding");
            }

            writer.WriteString("mode", settings.PttMode == PttMode.Toggle ? "toggle" : "hold");
            writer.WriteEndObject();
            WriteNullable(writer, "microphone", settings.Microphone);
            WriteNullable(writer, "outputDevice", settings.OutputDevice);
            writer.WriteNumber("cueVolume", settings.CueVolume);
            writer.WriteString("defaultUnit", settings.DefaultUnit);
            writer.WriteNumber("threshold", settings.Threshold);
            WriteNullable(writer, "interfaceLanguage", settings.InterfaceLanguage);
            writer.WriteString("theme", settings.Theme.ToString().ToLowerInvariant());
            writer.WriteEndObject();
        }

        stream.Write("\n"u8);
        return stream.ToArray();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string property, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(property);
        }
        else
        {
            writer.WriteString(property, value);
        }
    }
}
