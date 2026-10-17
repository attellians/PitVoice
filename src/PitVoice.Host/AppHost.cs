using PitVoice.Core.Asr;
using PitVoice.Core.Cues;
using PitVoice.Core.Events;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pipeline;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.Core.Responses;
using PitVoice.Core.Settings;
using PitVoice.Host.Diagnostics;
using PitVoice.IRacing.Diagnostics;
using PitVoice.Models;
using PitVoice.Speech.Asr;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PitVoice.Host;

/// <summary>Where the host finds packs, additions and models.</summary>
public sealed record AppHostOptions
{
    /// <summary>Directory with the built-in language packs.</summary>
    public required string PacksDirectory { get; init; }

    /// <summary>Directory with user additions; null means <see cref="UserAdditionsLoader.ResolveDirectory"/>.</summary>
    public string? AdditionsDirectory { get; init; }

    public required ModelStore Models { get; init; }

    /// <summary>How often the game is checked for the icon (design decision 3).</summary>
    public TimeSpan GamePollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Take <see cref="HostLock.PipelineHostName"/>; tests switch it off.</summary>
    public bool UseHostLock { get; init; } = true;

    /// <summary>Application events (logging spec); nothing about phrases is written to it. The caller owns it.</summary>
    public ILogger Log { get; init; } = Logger.None;
}

/// <summary>How <see cref="AppHost.StartAsync"/> ended.</summary>
public enum HostStartResult
{
    Started,

    /// <summary>Another pipeline host (the tray app or <c>run</c>) holds the lock.</summary>
    AlreadyRunning,
}

/// <summary>Whether a settings change reached the file; when not, it still applies until exit (spec: Settings file is read-only).</summary>
public sealed record SettingsSaveResult(bool Saved, string? Error)
{
    public static SettingsSaveResult Ok { get; } = new(true, null);
}

/// <summary>How a button assignment from the window ended.</summary>
/// <param name="Status">Assigned, cancelled (Esc, closed page) or timed out.</param>
/// <param name="Binding">The new binding when assigned.</param>
/// <param name="Error">Why no device could be listened to, or null.</param>
/// <param name="Save">Whether the new binding reached the settings file.</param>
public sealed record AssignmentResult(PttAssignmentStatus Status, PttBinding? Binding, string? Error, SettingsSaveResult? Save);

/// <summary>A built-in language the user can choose.</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
/// The pipeline host shared by the tray app and <c>pitvoice-cli run</c> (design decisions 1–4). It holds a session —
/// pack, recognizer, <see cref="VoicePipeline"/>, PTT source and state machine — built from the settings. A settings
/// change is saved at once and queued; it is applied when no phrase is recorded or processed, so a phrase in flight
/// finishes with the old settings (spec: Live apply). Several changes in a row are applied once. A language change loads
/// the pack and the model again; meanwhile a press gets the error cue with "model is loading". Everything else
/// rebuilds only the light parts and keeps the recognizer. The status for the icon is recomputed from what the host
/// knows, and the game is polled every <see cref="AppHostOptions.GamePollInterval"/>.
/// </summary>
public sealed class AppHost : IAsyncDisposable
{
    /// <summary>After a new game device source starts, how long to wait for the bound device to be reported.</summary>
    public static readonly TimeSpan DeviceCheckDelay = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromMilliseconds(50);

    private readonly AppHostOptions _options;
    private readonly ISettingsStore _store;
    private readonly IHostPlatform _platform;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly Lock _lock = new();
    private readonly PhraseIdSource _ids = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _applySignal = new(0);
    private readonly List<TaskCompletionSource> _applyWaiters = [];
    private IReadOnlyList<LanguagePackLoadResult> _packs = [];
    private Task _applyLoop = Task.CompletedTask;
    private HostLock? _hostLock;
    private ITimer? _gameTimer;
    private ITimer? _deviceCheck;
    private AppSettings _target;
    private AppSettings? _applied;
    private bool _reloadRequested;
    private bool _modelReleased;
    private bool _rebuildPtt;
    private bool _started;
    private HostFacts _facts = HostFacts.Initial;
    private HostStatus _status = HostStatus.Evaluate(HostFacts.Initial);

    // The session; replaced only by the apply loop.
    private LanguagePack? _pack;
    private PackAdditionsResult? _additions;
    private IReadOnlyList<string> _missingAdditionWords = [];
    private IAsrEngine? _engine;
    private IAudioCuePlayer? _cues;
    private (string? Device, int Volume) _cuesFor;
    private IPttHandler? _handler;
    private PttStateMachine? _machine;
    private IPttSource? _source;
    private PttBinding? _sourceBinding;
    private int _pausedPtt;

    public AppHost(AppHostOptions options, ISettingsStore store, IHostPlatform platform, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(platform);
        _options = options;
        _store = store;
        _platform = platform;
        _time = time ?? TimeProvider.System;
        _log = options.Log;
        AdditionsDirectory = UserAdditionsLoader.ResolveDirectory(options.AdditionsDirectory);
        Events = new EventBus(e =>
        {
            _log.Error(e, "Event subscriber failed");
            SubscriberFailed?.Invoke(e);
        });
        Events.Subscribe(Journal.OnEvent);
        Events.Subscribe(new PipelineIssueLogger(_log).OnEvent);
        _target = AppSettings.Defaults("en");
    }

    /// <summary>Pipeline events of every session; one bus for the whole run, phrase ids never repeat.</summary>
    public EventBus Events { get; }

    /// <summary>The last phrases of this run for the main page (spec: Recent phrases).</summary>
    public PhraseJournal Journal { get; } = new();

    /// <summary>A subscriber of <see cref="Events"/> threw.</summary>
    public event Action<Exception>? SubscriberFailed;

    /// <summary>The status changed; raised on a background thread.</summary>
    public event Action<HostStatus>? StatusChanged;

    /// <summary>Settings changed (saved or not); raised on the caller's thread.</summary>
    public event Action<AppSettings>? SettingsChanged;

    /// <summary>A new session was built (language, pack or additions may differ); raised on a background thread.</summary>
    public event Action? SessionChanged;

    /// <summary>The bound game device was connected or removed; raised on the source's thread.</summary>
    public event Action<PttDeviceChange>? PttDeviceChanged;

    public IHostPlatform Platform => _platform;

    public TimeProvider Time => _time;

    public ModelStore Models => _options.Models;

    public string AdditionsDirectory { get; }

    /// <summary>Path of the settings file.</summary>
    public string SettingsPath => _store.Path;

    /// <summary>Problems found when the settings file was read at start.</summary>
    public IReadOnlyList<string> SettingsWarnings { get; private set; } = [];

    /// <summary>Settings as the user set them last (they apply once the pipeline is free).</summary>
    public AppSettings Settings
    {
        get
        {
            lock (_lock)
            {
                return _target;
            }
        }
    }

    public HostStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    /// <summary>Built-in languages whose pack passed validation (spec: Language list).</summary>
    public IReadOnlyList<LanguageOption> Languages => _packs
        .Where(r => r.Pack is not null)
        .Select(r => new LanguageOption(r.Pack!.Language, r.Pack.Name))
        .OrderBy(l => l.Code, StringComparer.Ordinal)
        .ToArray();

    /// <summary>Pack of the current language with the user's additions, or null when it did not load.</summary>
    public LanguagePack? Pack
    {
        get
        {
            lock (_lock)
            {
                return _pack;
            }
        }
    }

    /// <summary>The additions file of the current language and its problems, or null without a pack.</summary>
    public PackAdditionsResult? Additions
    {
        get
        {
            lock (_lock)
            {
                return _additions;
            }
        }
    }

    /// <summary>Words of the additions the loaded model does not know (spec: Word outside model vocabulary).</summary>
    public IReadOnlyList<string> MissingAdditionWords
    {
        get
        {
            lock (_lock)
            {
                return _missingAdditionWords;
            }
        }
    }

    /// <summary>The recognizer with the loaded model, or null while there is none.</summary>
    public IAsrEngine? Engine
    {
        get
        {
            lock (_lock)
            {
                return _engine is { IsLoaded: true } engine ? engine : null;
            }
        }
    }

    /// <summary>Takes the host lock, reads the settings and packs and builds the first session.</summary>
    public async Task<HostStartResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_options.UseHostLock)
        {
            _hostLock = HostLock.TryAcquire(HostLock.PipelineHostName);
            if (_hostLock is null)
            {
                return HostStartResult.AlreadyRunning;
            }
        }

        // A host refused by the lock writes nothing: the log file belongs to the host that runs.
        _started = true;
        _log.Information("PitVoice {Version} started on {Os}, .NET {Runtime}", DiagnosticPaths.Version, DiagnosticPaths.OperatingSystem, Environment.Version);
        var loaded = _store.Load();
        SettingsWarnings = loaded.Warnings;
        lock (_lock)
        {
            _target = loaded.Settings;
        }

        _log.Information("Settings file {Path}", _store.Path);
        foreach (var warning in loaded.Warnings)
        {
            _log.Warning("Settings file: {Warning}", warning);
        }

        _packs = LanguagePackLoader.LoadDirectory(_options.PacksDirectory);
        foreach (var failed in _packs.Where(r => r.Pack is null))
        {
            _log.Error("Built-in language pack did not load: {Errors}", string.Join("; ", failed.Errors.Select(e => e.ToString())));
        }
        _applyLoop = Task.Run(ApplyLoopAsync, CancellationToken.None);
        await WhenAppliedAsync().WaitAsync(cancellationToken).ConfigureAwait(false);

        PollGame();
        _gameTimer = _time.CreateTimer(_ => PollGame(), null, _options.GamePollInterval, _options.GamePollInterval);
        return HostStartResult.Started;
    }

    /// <summary>
    /// Changes the settings: saved to the file at once, applied when the pipeline is free. A failed save is reported and
    /// the new settings still apply until exit.
    /// </summary>
    public SettingsSaveResult Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        AppSettings next;
        lock (_lock)
        {
            next = change(_target);
            if (next == _target)
            {
                return SettingsSaveResult.Ok;
            }

            _target = next;
        }

        var result = SettingsSaveResult.Ok;
        try
        {
            _store.Save(next);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result = new SettingsSaveResult(false, e.Message);
        }

        SettingsChanged?.Invoke(next);
        _applySignal.Release();
        return result;
    }

    /// <summary>Completes when every change made so far is applied.</summary>
    public Task WhenAppliedAsync()
    {
        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _applyWaiters.Add(waiter);
        }

        _applySignal.Release();
        return waiter.Task;
    }

    /// <summary>Loads the pack, the additions and the model of the current language again ("Reload", model downloaded or deleted).</summary>
    public Task ReloadAsync()
    {
        lock (_lock)
        {
            _modelReleased = false;
            _reloadRequested = true;
        }

        return WhenAppliedAsync();
    }

    /// <summary>
    /// Unloads the model and keeps it unloaded until <see cref="ReloadAsync"/>, so its files can be deleted (spec: Delete
    /// model in use). A press meanwhile gets the error cue.
    /// </summary>
    internal Task ReleaseModelAsync()
    {
        lock (_lock)
        {
            _modelReleased = true;
            _reloadRequested = true;
        }

        return WhenAppliedAsync();
    }

    /// <summary>Checks again whether the game runs as administrator (the window does it when it opens).</summary>
    public void RefreshElevation()
    {
        var game = Status.Facts.Game;
        var elevated = game != ConnectionState.NotRunning && CheckElevation();
        UpdateFacts(f => f with { GameElevated = elevated });
    }

    public async ValueTask DisposeAsync()
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        if (_started)
        {
            _log.Information("Host stopping");
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        _gameTimer?.Dispose();
        try
        {
            await _applyLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        IPttHandler? handler;
        lock (_lock)
        {
            handler = _handler;
            _handler = null;
            _deviceCheck?.Dispose();
        }

        DisposeSource();
        _machine?.Dispose();
        if (handler is not null)
        {
            await handler.DisposeAsync().ConfigureAwait(false);
        }

        (_cues as IDisposable)?.Dispose();
        (_engine as IDisposable)?.Dispose();
        await Events.DrainAsync().ConfigureAwait(false);
        Events.Dispose();
        _hostLock?.Dispose();
        _stopping.Dispose();
        if (_started)
        {
            _log.Information("Host stopped");
        }
    }

    /// <summary>
    /// Button assignment from the window (design decision 12): the PTT is paused so the current button does not start a
    /// recording, every device and the keyboard are listened to, the first press (except Esc) becomes the binding and is
    /// saved; Esc, <see cref="PttAssignment.Timeout"/> or <paramref name="cancellationToken"/> leave the binding as it was.
    /// </summary>
    public async Task<AssignmentResult> AssignPttAsync(CancellationToken cancellationToken = default)
    {
        await PausePttAsync().ConfigureAwait(false);
        try
        {
            using var assignment = new PttAssignment(_time);
            using var source = _platform.CreateAssignmentSource();
            source.Input += assignment.OnInput;
            if (source.Start() is { } error)
            {
                return new AssignmentResult(PttAssignmentStatus.Cancelled, null, error, null);
            }

            PttAssignmentResult result;
            try
            {
                result = await assignment.Result.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new AssignmentResult(PttAssignmentStatus.Cancelled, null, null, null);
            }

            if (result is not { Status: PttAssignmentStatus.Assigned, Binding: { } binding })
            {
                return new AssignmentResult(result.Status, null, null, null);
            }

            var save = Update(s => s with { PttBinding = binding });
            return new AssignmentResult(PttAssignmentStatus.Assigned, binding, null, save);
        }
        finally
        {
            await ResumePtt().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops the PTT from reaching the pipeline and closes its source (button assignment opens every device; two SDL
    /// sources must not run at once). <see cref="ResumePtt"/> rebuilds it.
    /// </summary>
    internal Task PausePttAsync()
    {
        Interlocked.Increment(ref _pausedPtt);
        return RebuildPttAsync();
    }

    internal Task ResumePtt()
    {
        Interlocked.Decrement(ref _pausedPtt);
        return RebuildPttAsync();
    }

    /// <summary>Builds the PTT part of the session again (through the apply loop, when the pipeline is free).</summary>
    private Task RebuildPttAsync()
    {
        lock (_lock)
        {
            _rebuildPtt = true;
        }

        return WhenAppliedAsync();
    }

    private bool IsPttPaused => Volatile.Read(ref _pausedPtt) > 0;

    private async Task ApplyLoopAsync()
    {
        var token = _stopping.Token;
        while (!token.IsCancellationRequested)
        {
            await _applySignal.WaitAsync(token).ConfigureAwait(false);
            while (true)
            {
                await WaitIdleAsync(token).ConfigureAwait(false);
                AppSettings target;
                AppSettings? previous;
                bool reload;
                bool rebuildPtt;
                List<TaskCompletionSource>? done = null;
                lock (_lock)
                {
                    target = _target;
                    previous = _applied;
                    reload = _reloadRequested;
                    rebuildPtt = _rebuildPtt;
                    _reloadRequested = false;
                    _rebuildPtt = false;
                    if (!reload && !rebuildPtt && SameSession(_applied, target))
                    {
                        _applied = target;
                        done = [.. _applyWaiters];
                        _applyWaiters.Clear();
                    }
                }

                if (done is not null)
                {
                    LogApplied(previous, target);
                    done.ForEach(w => w.TrySetResult());
                    break;
                }

                LogApplied(previous, target);
                var applied = _applied;
                if (reload || applied is null || applied.Language != target.Language)
                {
                    await RebuildFullAsync(target, token).ConfigureAwait(false);
                }
                else
                {
                    await RebuildLightAsync(target).ConfigureAwait(false);
                }

                lock (_lock)
                {
                    _applied = target;
                }

                SessionChanged?.Invoke();
            }
        }
    }

    /// <summary>One record per applied change; several changes applied at once give one record (spec: Settings changed).</summary>
    private void LogApplied(AppSettings? previous, AppSettings target)
    {
        if (previous == target)
        {
            return;
        }

        if (previous is null)
        {
            _log.Information("Settings at start: {Settings}", LogText.Describe(target));
        }
        else
        {
            _log.Information("Settings applied: {Settings}", LogText.Describe(target));
        }
    }

    /// <summary>Settings that only the window reads (interface language, theme) do not rebuild the session.</summary>
    private static bool SameSession(AppSettings? applied, AppSettings target) =>
        applied is not null
        && applied == target with { InterfaceLanguage = applied.InterfaceLanguage, Theme = applied.Theme };

    private async Task WaitIdleAsync(CancellationToken token)
    {
        while (IsBusy)
        {
            await Task.Delay(IdleCheckInterval, token).ConfigureAwait(false);
        }
    }

    private bool IsBusy
    {
        get
        {
            lock (_lock)
            {
                return (_handler?.IsBusy ?? false) || (_machine?.IsRecording ?? false);
            }
        }
    }

    private async Task RebuildFullAsync(AppSettings target, CancellationToken token)
    {
        var language = target.Language;
        var match = _packs.FirstOrDefault(r => r.Pack?.Language == language);
        LanguagePack? pack = null;
        PackAdditionsResult? additions = null;
        string? packError = null;
        if (match?.Pack is { } builtIn)
        {
            additions = UserAdditions.Apply(builtIn, AdditionsDirectory);
            pack = additions.Pack;
        }
        else
        {
            packError = match is null
                ? $"no language pack '{language}' in {_options.PacksDirectory}"
                : string.Join("; ", match.Errors.Select(e => e.ToString()));
        }

        var modelId = pack?.AsrModel;
        bool released;
        lock (_lock)
        {
            released = _modelReleased;
        }

        var modelPath = modelId is null || released ? null : _options.Models.GetInstalledPath(modelId);
        if (pack is not null && modelId is null)
        {
            packError = $"language pack '{language}' names no asr_model";
        }

        LogPack(language, packError, additions);
        if (packError is null && modelId is not null && modelPath is null)
        {
            if (released)
            {
                _log.Information("Model {ModelId} unloaded", modelId);
            }
            else
            {
                _log.Warning("Model {ModelId} is not installed", modelId);
            }
        }

        IAsrEngine? oldEngine;
        lock (_lock)
        {
            _pack = pack;
            _additions = additions;
            _missingAdditionWords = [];
            oldEngine = _engine;
            _engine = null;
        }

        UpdateFacts(f => f with
        {
            PackError = packError,
            ModelId = modelId,
            ModelInstalled = modelPath is not null,
            ModelLoading = modelPath is not null && packError is null,
            ModelError = null,
        });

        // While the model loads, a press gets the error cue with the reason (spec: Language switch).
        var reason = packError is not null ? StatusReason.PackFailed
            : modelPath is null ? StatusReason.ModelNotInstalled
            : StatusReason.ModelLoading;
        await SwapHandlerAsync(target, () => Unavailable(reason, packError ?? modelId)).ConfigureAwait(false);
        oldEngine?.Unload();
        (oldEngine as IDisposable)?.Dispose();

        if (pack is null || modelPath is null || packError is not null)
        {
            UpdateFacts(f => f with { Starting = false });
            return;
        }

        var engine = _platform.CreateEngine();
        var loadStarted = _time.GetTimestamp();
        AsrLoadResult loaded;
        try
        {
            loaded = await engine.LoadAsync(pack, modelPath, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            (engine as IDisposable)?.Dispose();
            throw;
        }

        if (!loaded.IsLoaded)
        {
            _log.Error("Model {ModelId} failed to load: {Error}", modelId, loaded.Error);
            (engine as IDisposable)?.Dispose();
            UpdateFacts(f => f with { ModelLoading = false, ModelError = loaded.Error, Starting = false });
            await SwapHandlerAsync(target, () => Unavailable(StatusReason.ModelFailed, loaded.Error)).ConfigureAwait(false);
            return;
        }

        var missing = engine is VoskAsrEngine { Vocabulary: { } vocabulary }
            ? GrammarBuilder.MissingAdditionWords(pack, vocabulary)
            : [];
        _log.Information("Model {ModelId} loaded in {Elapsed} ms", modelId, (long)_time.GetElapsedTime(loadStarted).TotalMilliseconds);
        if (missing.Count > 0)
        {
            _log.Warning("{Count} words of the user additions are not in the model vocabulary", missing.Count);
        }

        lock (_lock)
        {
            _engine = engine;
            _missingAdditionWords = missing;
        }

        UpdateFacts(f => f with { ModelLoading = false, Starting = false });
        await SwapHandlerAsync(target, () => CreatePipeline(target)).ConfigureAwait(false);
    }

    /// <summary>Pack errors and problems of the additions file by file and line; the patterns themselves stay out.</summary>
    private void LogPack(string language, string? packError, PackAdditionsResult? additions)
    {
        if (packError is not null)
        {
            _log.Error("Language pack {Language} not loaded: {Error}", language, packError);
            return;
        }

        _log.Information("Language pack {Language} loaded", language);
        if (additions is not { Exists: true })
        {
            return;
        }

        _log.Information(
            "User additions {File}: {State}, {Errors} errors, {Warnings} warnings",
            additions.File,
            additions.Applied ? "applied" : "not applied",
            additions.Errors.Count(),
            additions.Warnings.Count());
        foreach (var issue in additions.Issues)
        {
            _log.Write(
                issue.Severity == IssueSeverity.Error ? LogEventLevel.Error : LogEventLevel.Warning,
                "User additions {File}:{Line}: problem at {Location}",
                issue.File,
                issue.Line,
                issue.Path);
        }
    }

    private Task RebuildLightAsync(AppSettings target)
    {
        IPttHandler? current;
        lock (_lock)
        {
            current = _handler;
        }

        return current is UnavailableResponder unavailable
            ? SwapHandlerAsync(target, () => Unavailable(unavailable.Reason, unavailable.Detail))
            : SwapHandlerAsync(target, () => CreatePipeline(target));
    }

    /// <summary>Builds the cue player, the handler and the PTT for <paramref name="target"/> and replaces the old ones.</summary>
    private async Task SwapHandlerAsync(AppSettings target, Func<IPttHandler> create)
    {
        EnsureCues(target);
        var handler = create();
        IPttHandler? old;
        lock (_lock)
        {
            old = _handler;
            _handler = handler;
        }

        EnsurePtt(target, handler);
        if (old is not null)
        {
            await old.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void EnsureCues(AppSettings target)
    {
        if (_cues is not null && _cuesFor == (target.OutputDevice, target.CueVolume))
        {
            return;
        }

        var old = _cues;
        _cues = _platform.CreateCuePlayer(target.OutputDevice, target.CueVolume);
        _cuesFor = (target.OutputDevice, target.CueVolume);
        (old as IDisposable)?.Dispose();
    }

    private UnavailableResponder Unavailable(StatusReason reason, string? detail) =>
        new(reason, detail, _cues!, Events, _ids, _time);

    private PipelineHandler CreatePipeline(AppSettings target)
    {
        LanguagePack pack;
        IAsrEngine engine;
        lock (_lock)
        {
            pack = _pack!;
            engine = _engine!;
        }

        var executor = new PitExecutor(_platform.Telemetry, _platform.Sink, _time, _platform.ExecutionLog);
        return new PipelineHandler(new VoicePipeline(new VoicePipelineServices(
            _platform.Capture,
            target.Microphone,
            new SpeechRecognizer(engine),
            new CommandParser(pack),
            new ResponseBuilder(pack),
            target.ToParseOptions(),
            executor,
            _cues!,
            Events,
            _time)
        {
            PhraseIds = _ids,
        }));
    }

    /// <summary>A new state machine for every session (binding and mode may change); a new source only for another device.</summary>
    private void EnsurePtt(AppSettings target, IPttHandler handler)
    {
        if (IsPttPaused || target.PttBinding is not { } binding)
        {
            DisposeSource();
            Interlocked.Exchange(ref _machine, null)?.Dispose();
            UpdateFacts(f => f with { Binding = IsPttPaused ? f.Binding : null, PttSourceError = null });
            return;
        }

        if (_source is null || _sourceBinding is null || !SameDevice(_sourceBinding, binding))
        {
            StartSource(binding);
        }

        _sourceBinding = binding;
        var machine = new PttStateMachine(binding, target.PttMode, _time);
        machine.Transition += handler.OnTransition;
        Interlocked.Exchange(ref _machine, machine)?.Dispose();
        UpdateFacts(f => f with { Binding = binding });
    }

    private static bool SameDevice(PttBinding a, PttBinding b) => a.Kind == b.Kind
        && (a.Kind == PttDeviceKind.Keyboard || string.Equals(a.Key.Device, b.Key.Device, StringComparison.OrdinalIgnoreCase));

    private void StartSource(PttBinding binding)
    {
        DisposeSource();
        var source = _platform.CreatePttSource(binding);
        var seen = false;
        source.Input += input =>
        {
            if (ReferenceEquals(_source, source))
            {
                Volatile.Read(ref _machine)?.OnInput(input);
            }
        };
        source.DeviceChanged += change =>
        {
            if (!ReferenceEquals(_source, source))
            {
                return;
            }

            Volatile.Read(ref _machine)?.OnDeviceChanged(change);
            if (binding.IsDevice(change.Device))
            {
                _log.Information("PTT device {State}: {Name}", change.Connected ? "connected" : "disconnected", change.Name);
                Volatile.Write(ref seen, seen || change.Connected);
                UpdateFacts(f => f with { PttDeviceConnected = change.Connected });
                PttDeviceChanged?.Invoke(change);
            }
        };

        _source = source;
        var error = source.Start();
        if (error is null)
        {
            _log.Information("PTT source started for {Binding}", binding.Describe());
        }
        else
        {
            _log.Error("PTT source for {Binding} failed: {Error}", binding.Describe(), error);
        }

        UpdateFacts(f => f with { PttSourceError = error, PttDeviceConnected = true });
        if (error is null && binding.Kind == PttDeviceKind.Joystick)
        {
            // Devices present at start are reported shortly after; a device that is not reported is not connected.
            lock (_lock)
            {
                _deviceCheck?.Dispose();
                _deviceCheck = _time.CreateTimer(
                    _ =>
                    {
                        if (ReferenceEquals(_source, source) && !Volatile.Read(ref seen))
                        {
                            _log.Warning("PTT device is not connected: {Name}", binding.Name);
                            UpdateFacts(f => f with { PttDeviceConnected = false });
                        }
                    },
                    null,
                    DeviceCheckDelay,
                    Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void DisposeSource()
    {
        var source = Interlocked.Exchange(ref _source, null);
        _sourceBinding = null;
        source?.Dispose();
    }

    private void PollGame()
    {
        ConnectionState state;
        try
        {
            state = _platform.Telemetry.Current.State;
        }
#pragma warning disable CA1031 // The icon must survive any telemetry failure; the game counts as not running.
        catch (Exception)
#pragma warning restore CA1031
        {
            state = ConnectionState.NotRunning;
        }

        var previous = Status.Facts.Game;
        var elevated = Status.Facts.GameElevated;
        if (state == ConnectionState.NotRunning)
        {
            elevated = false;
        }
        else if (previous == ConnectionState.NotRunning)
        {
            elevated = CheckElevation();
        }

        UpdateFacts(f => f with { Game = state, GameElevated = elevated });
    }

    private bool CheckElevation()
    {
        try
        {
            return _platform.CheckElevation() == GameElevation.Elevated;
        }
#pragma warning disable CA1031 // Spec: Elevation cannot be determined — no hint, keep working.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    private void UpdateFacts(Func<HostFacts, HostFacts> change)
    {
        HostStatus status;
        HostFacts old;
        HostFacts facts;
        bool statusChanged;
        lock (_lock)
        {
            old = _facts;
            facts = change(old);
            if (facts == old)
            {
                return;
            }

            _facts = facts;
            status = HostStatus.Evaluate(facts);
            statusChanged = status != _status;
            _status = status;
        }

        LogGame(old, facts);
        if (statusChanged)
        {
            StatusChanged?.Invoke(status);
        }
    }

    /// <summary>Only changes of the game state are written, so polling an idle game leaves no records (spec: Idle game polling).</summary>
    private void LogGame(HostFacts old, HostFacts facts)
    {
        if (old.Game != facts.Game)
        {
            _log.Information("iRacing: {State}", facts.Game switch
            {
                ConnectionState.NotRunning => "not running",
                ConnectionState.Connected => "running, not in the car",
                _ => "in the car",
            });
        }

        if (old.GameElevated != facts.GameElevated && (facts.GameElevated || facts.Game != ConnectionState.NotRunning))
        {
            _log.Warning(facts.GameElevated
                ? "iRacing runs as administrator while PitVoice does not"
                : "iRacing no longer runs as administrator");
        }
    }
}
