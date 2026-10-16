using PitVoice.App.ViewModels;
using Velopack;
using Velopack.Sources;

namespace PitVoice.App;

/// <summary>
/// Releases of the project on GitHub through the installer library (design decision 4): no token, no pre-releases
/// (so the <c>models</c> mirror release is never taken for a version). The library verifies the package checksum.
/// </summary>
internal sealed class VelopackUpdateSource : Host.Updates.IUpdateSource
{
    private readonly UpdateManager _manager = new(new GithubSource(AboutViewModel.RepositoryUrl, accessToken: null, prerelease: false));
    private readonly Lock _lock = new();
    private Velopack.UpdateInfo? _found;

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<Host.Updates.UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        var found = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            _found = found;
        }

        if (found is null)
        {
            return null;
        }

        var version = found.TargetFullRelease.Version.ToString();
        return new Host.Updates.UpdateInfo(version, $"{AboutViewModel.RepositoryUrl}/releases/tag/v{version}");
    }

    public Task DownloadAsync(Host.Updates.UpdateInfo update, Action<int> progress, CancellationToken cancellationToken) =>
        _manager.DownloadUpdatesAsync(Found(update), progress, cancellationToken);

    public void ApplyAndRestart(Host.Updates.UpdateInfo update) => _manager.ApplyUpdatesAndRestart(Found(update).TargetFullRelease);

    private Velopack.UpdateInfo Found(Host.Updates.UpdateInfo update)
    {
        lock (_lock)
        {
            return _found is { } found && found.TargetFullRelease.Version.ToString() == update.Version
                ? found
                : throw new InvalidOperationException($"version {update.Version} was not found by the last check");
        }
    }
}
