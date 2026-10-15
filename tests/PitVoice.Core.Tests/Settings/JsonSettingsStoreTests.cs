using PitVoice.Core.Parsing;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using Xunit;

namespace PitVoice.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pitvoice-settings-" + Guid.NewGuid().ToString("N"));

    public JsonSettingsStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private JsonSettingsStore Store(string language = "ru") => new(FilePath, language);

    private SettingsLoadResult LoadFrom(string json)
    {
        File.WriteAllText(FilePath, json);
        return Store().Load();
    }

    [Fact]
    public void FirstRun_NoFile_GivesDefaults()
    {
        var result = Store("en").Load();

        Assert.Empty(result.Warnings);
        Assert.Null(result.BackupPath);
        var settings = result.Settings;
        Assert.Equal("en", settings.Language);
        Assert.Null(settings.PttBinding);
        Assert.Equal(PttMode.Hold, settings.PttMode);
        Assert.Null(settings.Microphone);
        Assert.Null(settings.OutputDevice);
        Assert.Equal(100, settings.CueVolume);
        Assert.Equal(FuelUnits.Liters, settings.DefaultUnit);
        Assert.Equal(ParseOptions.DefaultConfidenceThreshold, settings.Threshold);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void CorruptedFile_IsMovedToBak_AndDefaultsAreUsed()
    {
        var result = LoadFrom("{ \"language\": \"en\", ");

        Assert.Equal(FilePath + ".bak", result.BackupPath);
        Assert.True(File.Exists(FilePath + ".bak"));
        Assert.False(File.Exists(FilePath));
        Assert.Equal("ru", result.Settings.Language);
        Assert.Contains("not valid JSON", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void NotAnObject_IsTreatedAsCorrupted()
    {
        var result = LoadFrom("[1, 2]");

        Assert.NotNull(result.BackupPath);
        Assert.Equal(AppSettings.Defaults("ru"), result.Settings);
    }

    [Fact]
    public void MissingFields_GetDefaults()
    {
        var result = LoadFrom("""{ "language": "en" }""");

        Assert.Empty(result.Warnings);
        Assert.Equal(AppSettings.Defaults("en"), result.Settings);
    }

    [Fact]
    public void AllFields_AreRead()
    {
        var result = LoadFrom("""
            {
              "version": 1,
              "language": "en",
              "ptt": { "binding": { "kind": "joystick", "guid": "0300abc", "button": 5, "name": "Руль" }, "mode": "toggle" },
              "microphone": "Headset Mic",
              "outputDevice": "Headphones",
              "cueVolume": 40,
              "defaultUnit": "gallons",
              "threshold": 0.7
            }
            """);

        Assert.Empty(result.Warnings);
        var settings = result.Settings;
        Assert.Equal(new PttBinding(PttKey.JoystickButton("0300abc", 5), "Руль"), settings.PttBinding);
        Assert.Equal(PttMode.Toggle, settings.PttMode);
        Assert.Equal("Headset Mic", settings.Microphone);
        Assert.Equal("Headphones", settings.OutputDevice);
        Assert.Equal(40, settings.CueVolume);
        Assert.Equal(FuelUnits.Gallons, settings.DefaultUnit);
        Assert.Equal(0.7, settings.Threshold);
    }

    [Fact]
    public void FileWithByteOrderMark_IsRead()
    {
        File.WriteAllText(FilePath, """{ "language": "en", "cueVolume": 30 }""", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = Store().Load();

        Assert.Null(result.BackupPath);
        Assert.Equal(30, result.Settings.CueVolume);
    }

    [Fact]
    public void ModeMissing_IsHold()
    {
        var result = LoadFrom("""{ "ptt": { "binding": null } }""");

        Assert.Equal(PttMode.Hold, result.Settings.PttMode);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void UnknownMode_IsHold_WithWarning()
    {
        var result = LoadFrom("""{ "ptt": { "mode": "walkie" } }""");

        Assert.Equal(PttMode.Hold, result.Settings.PttMode);
        Assert.Contains("walkie", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("150", 100)]
    [InlineData("-5", 0)]
    public void OutOfRangeVolume_IsClamped_WithWarning(string value, int expected)
    {
        var result = LoadFrom($$"""{ "cueVolume": {{value}} }""");

        Assert.Equal(expected, result.Settings.CueVolume);
        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData("1.7")]
    [InlineData("-0.1")]
    [InlineData("\"high\"")]
    public void OutOfRangeThreshold_IsDefault_WithWarning(string value)
    {
        var result = LoadFrom($$"""{ "threshold": {{value}} }""");

        Assert.Equal(ParseOptions.DefaultConfidenceThreshold, result.Settings.Threshold);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void UnknownUnit_IsDefault_WithWarning()
    {
        var result = LoadFrom("""{ "defaultUnit": "barrels" }""");

        Assert.Equal(FuelUnits.Liters, result.Settings.DefaultUnit);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void InvalidBinding_IsNotAssigned_WithWarning()
    {
        var result = LoadFrom("""{ "ptt": { "binding": { "kind": "joystick", "button": 5 } } }""");

        Assert.Null(result.Settings.PttBinding);
        Assert.Contains("ptt bind", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var settings = AppSettings.Defaults("ru") with
        {
            PttBinding = new PttBinding(PttKey.KeyboardKey(0x7C), "F13"),
            PttMode = PttMode.Toggle,
            OutputDevice = "Динамики",
            CueVolume = 70,
        };

        Store().Save(settings);

        Assert.Equal(settings, Store().Load().Settings);
        Assert.Contains("Динамики", File.ReadAllText(FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_ReplacesExistingFile_WithoutLeftovers()
    {
        Store().Save(AppSettings.Defaults("ru"));
        Store().Save(AppSettings.Defaults("en"));

        Assert.Equal("en", Store().Load().Settings.Language);
        Assert.Equal(["settings.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_CreatesDirectory()
    {
        var path = Path.Combine(_directory, "nested", "settings.json");

        new JsonSettingsStore(path, "ru").Save(AppSettings.Defaults("ru"));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Path_ExplicitWinsOverEnvironment()
    {
        Assert.Equal(FilePath, JsonSettingsStore.ResolvePath(FilePath));
        Assert.EndsWith(Path.Combine("PitVoice", "settings.json"), JsonSettingsStore.DefaultPath, StringComparison.Ordinal);
    }
}
