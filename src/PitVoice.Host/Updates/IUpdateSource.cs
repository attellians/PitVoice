namespace PitVoice.Host.Updates;

/// <summary>A newer version in the project releases.</summary>
/// <param name="Version">Version without the <c>v</c>, e.g. <c>0.1.3</c>.</param>
/// <param name="ReleaseUrl">Page of the release, opened by the link in the update block.</param>
public sealed record UpdateInfo(string Version, string ReleaseUrl);

/// <summary>
/// Where updates come from (design decision 4): the installer library in the app, a fake in the tests. Nothing about
/// the user goes into the requests.
/// </summary>
public interface IUpdateSource
{
    /// <summary>False for a build that was not installed by the installer; such a build never checks (spec: Developer build).</summary>
    bool IsInstalled { get; }

    /// <summary>The newest release when it is newer than the running version, or null when this one is the latest.</summary>
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads and verifies the package of <paramref name="update"/>; <paramref name="progress"/> gets 0..100.</summary>
    Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellationToken);

    /// <summary>Installs the downloaded <paramref name="update"/> and starts it once this process ends.</summary>
    void ApplyAndRestart(UpdateInfo update);
}
