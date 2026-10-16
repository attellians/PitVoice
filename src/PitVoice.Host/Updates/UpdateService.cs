using PitVoice.Core.Settings;
using PitVoice.Host.Diagnostics;
using Serilog;
using Serilog.Core;

namespace PitVoice.Host.Updates;

/// <summary>Where the update check stands.</summary>
public enum UpdateStatus
{
    /// <summary>Not an installed app: no checks at all (spec: Update check only in the installed app).</summary>
    NotInstalled,

    /// <summary>Nothing checked yet in this run.</summary>
    Idle,

    /// <summary>A manual check runs; automatic checks do not show it.</summary>
    Checking,

    UpToDate,

    /// <summary><see cref="UpdateState.Update"/> can be installed; <see cref="UpdateState.Error"/> holds why the last download failed.</summary>
    Available,

    Downloading,

    /// <summary>The manual check failed; automatic failures only go to the log.</summary>
    Failed,
}

/// <summary>What the window shows about updates.</summary>
/// <param name="Status">Where the check stands.</param>
/// <param name="Update">The newer version, when there is one.</param>
/// <param name="Percent">Download progress, 0..100.</param>
/// <param name="Error">Why the manual check or the download failed.</param>
public sealed record UpdateState(UpdateStatus Status, UpdateInfo? Update = null, int Percent = 0, string? Error = null);

/// <summary>
/// Update checks of the installed app (design decision 4, app-updates spec): at start and every 24 hours while the
/// "Check for updates" setting is on, and by the button in "About" whatever the setting. Checks run in the background
/// and never touch the pipeline. A failed automatic check is only logged; a failed manual check is shown. The update
/// is downloaded and installed only by <see cref="UpdateAndRestartAsync"/>, which stops the host first.
/// </summary>
public sealed class UpdateService : IAsyncDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly IUpdateSource _source;
    private readonly AppHost _host;
    private readonly Func<Task> _stopHost;
    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _checking = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _lock = new();
    private UpdateState _state;
    private ITimer? _timer;
    private bool _enabled;
    private bool _started;
    private Task _lastCheck = Task.CompletedTask;

    /// <param name="source">Releases of the project.</param>
    /// <param name="host">Gives the setting, its changes and the clock.</param>
    /// <param name="stopHost">Stops the host and everything that holds files before the new version is installed.</param>
    /// <param name="log">Application log; its failures never stop a check.</param>
    public UpdateService(IUpdateSource source, AppHost host, Func<Task> stopHost, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(stopHost);
        _source = source;
        _host = host;
        _stopHost = stopHost;
        _log = log ?? Logger.None;
        _time = host.Time;
        _state = new UpdateState(source.IsInstalled ? UpdateStatus.Idle : UpdateStatus.NotInstalled);
    }

    /// <summary>The state changed; raised on any thread.</summary>
    public event Action<UpdateState>? Changed;

    public UpdateState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>True for an installed app; otherwise the window shows that updates work only in the installed version.</summary>
    public bool IsInstalled => _source.IsInstalled;

    /// <summary>The check that ran last, for tests.</summary>
    internal Task LastCheck
    {
        get
        {
            lock (_lock)
            {
                return _lastCheck;
            }
        }
    }

    /// <summary>Checks now when the setting is on and follows its changes from then on.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (!_source.IsInstalled)
        {
            Write(() => _log.Information("Update check off: not an installed app"));
            return;
        }

        _host.SettingsChanged += OnSettingsChanged;
        SetEnabled(_host.Settings.CheckForUpdates);
    }

    /// <summary>The button in "About": checks whatever the setting and shows the result, failures included.</summary>
    public async Task<UpdateState> CheckNowAsync()
    {
        if (!_source.IsInstalled || State.Status == UpdateStatus.Downloading)
        {
            return State;
        }

        await _checking.WaitAsync(_stopping.Token).ConfigureAwait(false);
        try
        {
            var previous = State;
            SetState(new UpdateState(UpdateStatus.Checking, previous.Update));
            var task = CheckCoreAsync(manual: true, previous);
            lock (_lock)
            {
                _lastCheck = task;
            }

            await task.ConfigureAwait(false);
            return State;
        }
        finally
        {
            _checking.Release();
        }
    }

    /// <summary>
    /// The button in the update block: downloads with progress, then stops the host and starts the new version. When
    /// the download fails the update is offered again with the error, and the app keeps working in this version.
    /// </summary>
    public async Task UpdateAndRestartAsync()
    {
        UpdateInfo update;
        lock (_lock)
        {
            if (_state is not { Status: UpdateStatus.Available, Update: { } available })
            {
                return;
            }

            update = available;
            _state = new UpdateState(UpdateStatus.Downloading, update);
        }

        RaiseChanged();
        Write(() => _log.Information("Update {Version:l} download started", update.Version));
        try
        {
            await _source.DownloadAsync(update, OnProgress, _stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
#pragma warning disable CA1031 // Network, disk and package errors end the same way: the update is offered again.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Write(() => _log.Warning("Update {Version:l} download failed: {Reason}", update.Version, e.Message));
            SetState(new UpdateState(UpdateStatus.Available, update, Error: e.Message));
            return;
        }

        Write(() => _log.Information("Update {Version:l} downloaded, installing and restarting", update.Version));
        await _stopHost().ConfigureAwait(false);
        _source.ApplyAndRestart(update);
    }

    public async ValueTask DisposeAsync()
    {
        _host.SettingsChanged -= OnSettingsChanged;
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await LastCheck.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _stopping.Dispose();
        _checking.Dispose();
    }

    private void OnSettingsChanged(AppSettings settings) => SetEnabled(settings.CheckForUpdates);

    private void SetEnabled(bool enabled)
    {
        lock (_lock)
        {
            if (enabled == _enabled && (_timer is not null || !enabled))
            {
                return;
            }

            _enabled = enabled;
            _timer?.Dispose();
            _timer = null;
            if (enabled)
            {
                // Due now: the first check runs at start (or when the user turns the setting on), then every 24 hours.
                _timer = _time.CreateTimer(_ => CheckAutomatically(), null, TimeSpan.Zero, CheckInterval);
            }
        }

        Write(() => _log.Information("Automatic update check {State:l}", enabled ? "on" : "off"));
    }

    private void CheckAutomatically()
    {
        if (_stopping.IsCancellationRequested || State.Status == UpdateStatus.Downloading || !_checking.Wait(0))
        {
            return;
        }

        var task = Task.Run(async () =>
        {
            try
            {
                await CheckCoreAsync(manual: false, State).ConfigureAwait(false);
            }
            finally
            {
                _checking.Release();
            }
        });
        lock (_lock)
        {
            _lastCheck = task;
        }
    }

    private async Task CheckCoreAsync(bool manual, UpdateState previous)
    {
        var kind = manual ? "Manual" : "Automatic";
        UpdateInfo? update;
        try
        {
            update = await _source.CheckAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
#pragma warning disable CA1031 // No network, rate limit, broken release list: all are a failed check, never a crash.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Write(() => _log.Warning("{Kind:l} update check failed (current {Current:l}): {Reason}", kind, DiagnosticPaths.Version, e.Message));
            SetState(manual ? new UpdateState(UpdateStatus.Failed, previous.Update, Error: e.Message) : previous);
            return;
        }

        if (update is null)
        {
            Write(() => _log.Information("{Kind:l} update check: {Current:l} is the latest", kind, DiagnosticPaths.Version));
            SetState(new UpdateState(UpdateStatus.UpToDate));
        }
        else
        {
            Write(() => _log.Information("{Kind:l} update check: version {Version:l} found (current {Current:l})", kind, update.Version, DiagnosticPaths.Version));
            SetState(new UpdateState(UpdateStatus.Available, update));
        }
    }

    private void OnProgress(int percent)
    {
        lock (_lock)
        {
            if (_state.Status != UpdateStatus.Downloading || _state.Percent == percent)
            {
                return;
            }

            _state = _state with { Percent = Math.Clamp(percent, 0, 100) };
        }

        RaiseChanged();
    }

    private void SetState(UpdateState state)
    {
        lock (_lock)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
        }

        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(State);

    /// <summary>A broken log never breaks a check (spec: Log unavailable).</summary>
    private static void Write(Action write)
    {
        try
        {
            write();
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
