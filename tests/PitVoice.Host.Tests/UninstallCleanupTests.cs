using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>Data removal on uninstall, on temporary directories (design decision 3, spec: Uninstall removes all user data).</summary>
public sealed class UninstallCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pitvoice-uninstall-" + Guid.NewGuid().ToString("N"));

    public UninstallCleanupTests()
    {
        Roaming = Path.Combine(_root, "Roaming", "PitVoice");
        Local = Path.Combine(_root, "Local", "PitVoice");
        Write(Roaming, "settings.json");
        Write(Roaming, "languages", "ru.yaml");
        Write(Local, "models", "vosk-model-small-ru-0.22", "am", "final.mdl");
        Write(Local, "models", "vosk-model-small-ru-0.22", ".pitvoice-model.json");
        Write(Local, "logs", "pitvoice-20261016.log");
        Write(Local, "logs", "crash-20261016-120000.log");
        Write(Local, "diagnostics", "pitvoice-diagnostics-20261016-120000.zip");
        Write(_root, "Local", "PitVoiceApp", "current", "PitVoice.exe"); // the program; the installer removes it itself
    }

    private string Roaming { get; }

    private string Local { get; }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Uninstall_removes_settings_additions_models_logs_and_diagnostics()
    {
        var failures = Cleanup().Run();

        Assert.Empty(failures);
        Assert.False(Directory.Exists(Roaming));
        Assert.False(Directory.Exists(Local));
        Assert.True(File.Exists(Path.Combine(_root, "Local", "PitVoiceApp", "current", "PitVoice.exe")));
    }

    [Fact]
    public void Missing_directories_are_fine()
    {
        Directory.Delete(_root, recursive: true);

        Assert.Empty(Cleanup().Run());
    }

    [Fact]
    public void File_locked_by_another_program_stays_and_the_rest_goes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // only Windows refuses to delete an open file
        }

        var locked = Path.Combine(Local, "diagnostics", "pitvoice-diagnostics-20261016-120000.zip");
        IReadOnlyList<string> failures;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            failures = Cleanup().Run();
        }

        Assert.Contains(failures, f => f.StartsWith(locked, StringComparison.Ordinal));
        Assert.False(Directory.Exists(Roaming));
        Assert.False(Directory.Exists(Path.Combine(Local, "models")));
        Assert.False(Directory.Exists(Path.Combine(Local, "logs")));
        Assert.Equal([locked], Directory.GetFiles(Path.Combine(Local, "diagnostics")));
    }

    [Fact]
    public void Developer_directories_are_kept()
    {
        var developerModels = Path.Combine(_root, "dev-models");
        Write(developerModels, "vosk-model-small-en-us-0.15", "am", "final.mdl");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(Local, "models", "linked"), developerModels);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Windows without developer mode cannot create links; the directory outside the data root is still checked.
        }

        Assert.Empty(Cleanup().Run());

        Assert.True(File.Exists(Path.Combine(developerModels, "vosk-model-small-en-us-0.15", "am", "final.mdl")));
        Assert.False(Directory.Exists(Path.Combine(Local, "models")));
    }

    [Fact]
    public void Default_directories_come_from_the_user_profile_not_from_the_environment()
    {
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PitVoice");
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PitVoice");

        Assert.Equal(
            [roaming, local],
            UninstallCleanup.Default.Directories);
    }

    private UninstallCleanup Cleanup() => new(Roaming, Local);

    private static void Write(string directory, params string[] parts)
    {
        var path = Path.Combine([directory, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "data");
    }
}
