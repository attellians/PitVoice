namespace PitVoice.Host;

/// <summary>
/// Removes the user's data when PitVoice is uninstalled (design decision 3, spec: Uninstall removes all user data):
/// <c>%AppData%\PitVoice</c> (settings, phrase additions) and the data directories of <c>%LocalAppData%\PitVoice</c>
/// (<c>models</c>, <c>logs</c>, <c>diagnostics</c>); the installer removes the rest of that directory itself. Only the
/// default paths are used, never the environment variables, so directories a developer pointed elsewhere stay. Every
/// file is deleted on its own: a locked file stays and the rest goes. Links to directories are removed, not followed.
/// No log: the log directory is among the deleted ones.
/// </summary>
public sealed class UninstallCleanup
{
    public static readonly IReadOnlyList<string> LocalDataDirectories = ["models", "logs", "diagnostics"];

    /// <param name="roamingRoot">The <c>%AppData%\PitVoice</c> directory, removed as a whole.</param>
    /// <param name="localRoot">The <c>%LocalAppData%\PitVoice</c> directory, of which <see cref="LocalDataDirectories"/> are removed.</param>
    public UninstallCleanup(string roamingRoot, string localRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roamingRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(localRoot);
        Directories = [roamingRoot, .. LocalDataDirectories.Select(d => Path.Combine(localRoot, d))];
    }

    /// <summary>The default directories of the current Windows user.</summary>
    public static UninstallCleanup Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PitVoice"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PitVoice"));

    /// <summary>Directories removed with everything in them.</summary>
    public IReadOnlyList<string> Directories { get; }

    /// <summary>Deletes what it can; returns one line per file or directory that stayed. Never throws.</summary>
    public IReadOnlyList<string> Run()
    {
        var failures = new List<string>();
        foreach (var directory in Directories)
        {
            DeleteDirectory(new DirectoryInfo(directory), failures);
        }

        return failures;
    }

    private static void DeleteDirectory(DirectoryInfo directory, List<string> failures)
    {
        try
        {
            if (!directory.Exists)
            {
                return;
            }

            if (directory.LinkTarget is null)
            {
                foreach (var file in directory.EnumerateFiles())
                {
                    Try(() =>
                    {
                        file.Attributes = FileAttributes.Normal;
                        file.Delete();
                    }, file.FullName, failures);
                }

                foreach (var child in directory.EnumerateDirectories())
                {
                    DeleteDirectory(child, failures);
                }
            }

            // A link goes on its own, the directory it points to stays; a directory that still holds a locked file stays too.
            if (directory.LinkTarget is not null || !directory.EnumerateFileSystemInfos().Any())
            {
                Try(() => directory.Delete(), directory.FullName, failures);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            failures.Add($"{directory.FullName}: {e.Message}");
        }
    }

    private static void Try(Action delete, string path, List<string> failures)
    {
        try
        {
            delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            failures.Add($"{path}: {e.Message}");
        }
    }
}
