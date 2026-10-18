using PitVoice.Core.Cues;
using PitVoice.Core.Events;
using PitVoice.Core.Pit;
using PitVoice.Core.Settings;

namespace PitVoice.Host;

/// <summary>What the indicator draws: idle, a phrase in progress or the result of the phrase.</summary>
public enum IndicatorVisual
{
    Idle,
    Listening,
    Recognizing,
    Done,
    NotUnderstood,
    NoGame,
    Error,
}

/// <summary>A snapshot for the overlay window.</summary>
/// <param name="Visual">State to draw.</param>
/// <param name="Visible">Whether the window is shown at all.</param>
/// <param name="Moving">Move mode: the window takes the mouse and has a dashed frame.</param>
public sealed record IndicatorView(IndicatorVisual Visual, bool Visible, bool Moving)
{
    public bool IsResult => Visual is IndicatorVisual.Done or IndicatorVisual.NotUnderstood or IndicatorVisual.NoGame or IndicatorVisual.Error;
}

/// <summary>
/// State of the indicator above the game (design D1, D2), computed without WPF from the pipeline events, the connection
/// to the game and the overlay settings. A press starts a new phrase and pushes out the previous one; the result is
/// remembered from the parse and execution events and shown with the result cue (<see cref="ResultSignaled"/>, or
/// <see cref="PipelineFailed"/> of the cue stage when the cue did not play), then held for <see cref="ResultHold"/>.
/// <see cref="Changed"/> is raised under the model's lock, in order, on whatever thread caused the change; the handler
/// must only hand the view over (e.g. post it to the UI thread).
/// </summary>
public sealed class IndicatorModel : IDisposable
{
    public static readonly TimeSpan ResultHold = TimeSpan.FromSeconds(2.5);

    private readonly TimeProvider _time;
    private readonly Action<Exception>? _onHandlerError;
    private readonly Lock _lock = new();
    private IndicatorVisual _visual = IndicatorVisual.Idle;
    private int _phrase;
    private IndicatorVisual? _pending;
    private bool _phraseInCar;
    private int _hold;
    private ITimer? _holdTimer;
    private ConnectionState _game;
    private OverlaySettings _settings;
    private bool _moving;
    private IndicatorView _view;
    private Action? _detach;

    /// <param name="time">Clock of the result hold.</param>
    /// <param name="settings">Overlay settings at start.</param>
    /// <param name="game">Connection to the game at start.</param>
    /// <param name="onHandlerError">
    /// Gets an exception thrown by a <see cref="Changed"/> handler, so the hold timer and the host status never see it;
    /// without it the exception reaches the caller.
    /// </param>
    public IndicatorModel(TimeProvider time, OverlaySettings settings, ConnectionState game, Action<Exception>? onHandlerError = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(settings);
        _time = time;
        _onHandlerError = onHandlerError;
        _settings = settings;
        _game = game;
        _view = Compute();
    }

    /// <summary>The view changed; raised under the lock (see the class remarks).</summary>
    public event Action<IndicatorView>? Changed;

    public IndicatorView View
    {
        get
        {
            lock (_lock)
            {
                return _view;
            }
        }
    }

    public OverlaySettings Settings
    {
        get
        {
            lock (_lock)
            {
                return _settings;
            }
        }
    }

    /// <summary>Move mode (design D7): not a setting, it ends with "Done" or with the app.</summary>
    public bool Moving
    {
        get
        {
            lock (_lock)
            {
                return _moving;
            }
        }

        set
        {
            lock (_lock)
            {
                _moving = value && _settings.Enabled;
                Publish();
            }
        }
    }

    public void OnEvent(PipelineEvent pipelineEvent)
    {
        ArgumentNullException.ThrowIfNull(pipelineEvent);
        lock (_lock)
        {
            if (pipelineEvent is PttPressed pressed)
            {
                Start(pressed.PhraseId);
                return;
            }

            if (pipelineEvent.PhraseId != _phrase)
            {
                return;
            }

            switch (pipelineEvent)
            {
                case PttReleased { Ignored: true } when _visual == IndicatorVisual.Listening:
                    Show(IndicatorVisual.Idle);
                    break;
                case PttReleased when _visual == IndicatorVisual.Listening:
                    Show(IndicatorVisual.Recognizing);
                    break;
                case CommandsApplied:
                    _pending = IndicatorVisual.Done;
                    break;
                case CommandsRejected:
                    _pending = IndicatorVisual.NotUnderstood;
                    break;
                case ApplyFailed { Outcome: ExecutionOutcome.NotConnected }:
                    _pending = IndicatorVisual.NoGame;
                    break;
                case ApplyFailed:
                    _pending = IndicatorVisual.Error;
                    break;
                case PipelineFailed { Stage: PipelineStage.Cue } when _visual == IndicatorVisual.Recognizing:
                    // The result cue did not play: the result is shown without it. A failed start cue does not end the phrase.
                    ShowResult(_pending ?? IndicatorVisual.Error);
                    break;
                case PipelineFailed { Stage: not PipelineStage.Cue }:
                    _pending = IndicatorVisual.Error;
                    break;
                case ResultSignaled { Cancelled: false } signaled when _visual == IndicatorVisual.Recognizing:
                    ShowResult(_pending ?? (signaled.Cue == Cue.Success ? IndicatorVisual.Done : IndicatorVisual.Error));
                    break;
                case PttUnavailable:
                    ShowResult(IndicatorVisual.Error);
                    break;
            }
        }
    }

    /// <summary>The connection to the game from the host status.</summary>
    public void OnGame(ConnectionState game)
    {
        lock (_lock)
        {
            _game = game;
            Publish();
        }
    }

    public void Apply(OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_lock)
        {
            _settings = settings;
            _moving &= settings.Enabled;
            Publish();
        }
    }

    public void Dispose()
    {
        Action? detach;
        lock (_lock)
        {
            detach = _detach;
            _detach = null;
            _holdTimer?.Dispose();
            _holdTimer = null;
        }

        detach?.Invoke();
    }

    /// <summary>Set by <see cref="AppHost.CreateIndicator"/>: removes the subscriptions on <see cref="Dispose"/>.</summary>
    internal void OnDispose(Action detach)
    {
        lock (_lock)
        {
            _detach = detach;
        }
    }

    private void Start(int phrase)
    {
        _phrase = phrase;
        _pending = null;
        _phraseInCar = _game == ConnectionState.InCar;
        Show(IndicatorVisual.Listening);
    }

    private void ShowResult(IndicatorVisual result)
    {
        Show(result);
        var hold = _hold;
        _holdTimer = _time.CreateTimer(_ => EndHold(hold), null, ResultHold, Timeout.InfiniteTimeSpan);
    }

    private void EndHold(int hold)
    {
        lock (_lock)
        {
            if (hold == _hold)
            {
                Show(IndicatorVisual.Idle);
            }
        }
    }

    private void Show(IndicatorVisual visual)
    {
        // Any change ends the hold of the previous result; a timer that still fires sees another hold number.
        _hold++;
        _holdTimer?.Dispose();
        _holdTimer = null;
        _visual = visual;
        Publish();
    }

    private void Publish()
    {
        var view = Compute();
        if (view == _view)
        {
            return;
        }

        _view = view;
        if (_onHandlerError is null)
        {
            Changed?.Invoke(view);
            return;
        }

        try
        {
            Changed?.Invoke(view);
        }
#pragma warning disable CA1031 // A failing window must not stop the pipeline, the status or the hold timer.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _onHandlerError(e);
        }
    }

    /// <summary>
    /// Spec: Visibility by game state — shown only in the car (user decision 2026-10-18). A phrase started in the car is
    /// shown to the end of its result even when the driver leaves the car or the game closes; a phrase started outside the
    /// car is not shown. Move mode shows the indicator anywhere.
    /// </summary>
    private IndicatorView Compute()
    {
        if (!_settings.Enabled)
        {
            return new IndicatorView(_visual, Visible: false, Moving: false);
        }

        var visible = _moving || (_visual == IndicatorVisual.Idle ? _game == ConnectionState.InCar : _phraseInCar);
        return new IndicatorView(_visual, visible, _moving);
    }
}
