using System.Windows.Input;
using Microsoft.Win32;
using PitVoice.App.Localization;
using PitVoice.App.ViewModels;
using PitVoice.Core.Settings;
using PitVoice.Host;

namespace PitVoice.App.Overlay;

/// <summary>
/// Draws <see cref="IndicatorModel"/> in <see cref="IndicatorWindow"/> on the UI thread (overlay-indicator spec): shows and
/// hides the window, keeps the icon center where the settings put it (design D5, D6), and runs move mode with the "Done"
/// button (design D7). The window is created on the first show and only hidden afterwards.
/// </summary>
internal sealed class OverlayController : IDisposable
{
    private readonly IndicatorModel _model;
    private readonly IndicatorControl _control;
    private IndicatorWindow? _window;
    private DoneWindow? _done;
    private OverlaySettings _settings;
    private ScreenPoint _center;
    private MonitorArea? _monitor;
    private bool _dragging;
    private ScreenPoint _grab;
    private double _wordRoom;
    private bool _disposed;

    public OverlayController(IndicatorModel model, IndicatorControl control)
    {
        _model = model;
        _control = control;
        _settings = control.Settings;
        _model.Changed += OnViewChanged;
        _control.Changed += OnSettingsChanged;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Render();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _model.Changed -= OnViewChanged;
        _control.Changed -= OnSettingsChanged;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _done?.Close();
        _window?.Close();
    }

    /// <summary>The icon is dragged in move mode: its center and monitor before the position is saved (add-vr-overlay D7).</summary>
    public event Action<ScreenPoint, MonitorArea>? Dragged;

    /// <summary>The word of kind B for a state (design D9).</summary>
    public static string Word(IndicatorVisual visual) => visual == IndicatorVisual.Idle ? string.Empty : Loc.T($"overlay.{visual}");

    private void OnViewChanged(IndicatorView view) => Ui.Post(Render);

    private void OnSettingsChanged()
    {
        var settings = _control.Settings;
        if (settings == _settings)
        {
            return;
        }

        _settings = settings;
        if (_window is { IsVisible: true })
        {
            Arrange(animate: true);
            Place();
        }
    }

    private void OnLanguageChanged()
    {
        if (_window is { IsVisible: true })
        {
            Arrange(animate: false);
            Place();
            _window.Badge.Show(_model.View.Visual, Word(_model.View.Visual), animate: false);
        }

        if (_done is not null)
        {
            _done.Button.Content = Loc.T("OverlayDone");
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Ui.Post(() =>
    {
        if (_window is { IsVisible: true } && !_dragging)
        {
            Arrange(animate: false);
            Place();
        }
    });

    private void Render()
    {
        if (_disposed)
        {
            return;
        }

        var view = _model.View;
        if (!view.Visible)
        {
            EndDrag(save: false);
            if (_window is { IsVisible: true })
            {
                _window.Badge.StopAll();
                _window.Hide();
            }

            _done?.Hide();
            return;
        }

        var window = EnsureWindow();
        var appearing = !window.IsVisible;
        if (appearing)
        {
            Arrange(animate: false);
            window.Show();
            Place();
        }

        window.Badge.Show(view.Visual, Word(view.Visual), animate: !appearing);
        SetMoving(view.Moving);
        OverlayNative.BringToTop(window.Handle);
    }

    private IndicatorWindow EnsureWindow()
    {
        if (_window is not null)
        {
            return _window;
        }

        var window = new IndicatorWindow();
        window.ScaleChanged += () =>
        {
            if (!_dragging)
            {
                Place();
            }
        };
        window.MouseLeftButtonDown += OnMouseDown;
        window.MouseMove += OnMouseMove;
        window.MouseLeftButtonUp += (_, _) => EndDrag(save: true);
        window.LostMouseCapture += (_, _) => EndDrag(save: true);
        _window = window;
        return window;
    }

    /// <summary>Center, monitor, kind, room for the word, size and opacity from the settings.</summary>
    private void Arrange(bool animate)
    {
        var badge = _window!.Badge;
        var monitors = OverlayNative.GetMonitors();
        if (monitors.Count == 0)
        {
            return;
        }

        var longest = Enum.GetValues<IndicatorVisual>().Max(v => IndicatorBadge.MeasureWord(Word(v), 1));
        _wordRoom = IndicatorBadge.WordRoomFor(_settings.Kind, longest);
        if (!_dragging)
        {
            (_center, _monitor) = OverlayLayout.Resolve(_settings.Position, monitors, _settings.Scale, _wordRoom);
        }

        badge.Arrange(_settings.Kind, longest);
        badge.Scale = _settings.Scale / 100.0;
        badge.SetOpacity(_settings.Opacity / 100.0, animate);
    }

    /// <summary>Moves the window so the icon center is at <see cref="_center"/>.</summary>
    private void Place()
    {
        if (_window is not { Handle: not 0 } window || _monitor is null)
        {
            return;
        }

        window.UpdateLayout();
        var badge = window.Badge;
        var offset = OverlayLayout.IconCenterInWindow * badge.Scale * _monitor.Scale;
        OverlayNative.MoveTo(window.Handle, (int)Math.Round(_center.X - offset), (int)Math.Round(_center.Y - offset));
        PlaceDone();
    }

    private void SetMoving(bool moving)
    {
        var window = _window!;
        window.ClickThrough = !moving;
        window.Badge.ShowFrame = moving;
        window.Cursor = moving ? Cursors.SizeAll : null;
        if (!moving)
        {
            EndDrag(save: true);
            _done?.Hide();
            return;
        }

        _done ??= new DoneWindow(() => _control.Moving = false);
        _done.Button.Content = Loc.T("OverlayDone");
        if (!_done.IsVisible)
        {
            _done.Show();
        }

        PlaceDone();
        OverlayNative.BringToTop(_done.Handle);
    }

    private void PlaceDone()
    {
        if (_done is not { IsVisible: true, Handle: not 0 } done || _monitor is null)
        {
            return;
        }

        done.UpdateLayout();
        var rect = OverlayLayout.DoneButton(
            _center,
            _settings.Scale,
            _monitor,
            (int)Math.Round(done.ActualWidth * _monitor.Scale),
            (int)Math.Round(done.ActualHeight * _monitor.Scale));
        OverlayNative.MoveTo(done.Handle, rect.Left, rect.Top);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_model.View.Moving || _window is null)
        {
            return;
        }

        var cursor = OverlayNative.CursorPosition();
        _grab = new ScreenPoint(cursor.X - _center.X, cursor.Y - _center.Y);
        _dragging = _window.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var monitors = OverlayNative.GetMonitors();
        if (monitors.Count == 0)
        {
            return;
        }

        var cursor = OverlayNative.CursorPosition();
        var wanted = new ScreenPoint(cursor.X - _grab.X, cursor.Y - _grab.Y);
        var monitor = OverlayLayout.NearestMonitor(wanted, monitors);
        _center = OverlayLayout.Clamp(wanted, monitor, _settings.Scale, _wordRoom);
        _monitor = monitor;
        Place();
        Dragged?.Invoke(_center, monitor);
    }

    /// <summary>The position is saved when the mouse is released (spec: Moving the indicator).</summary>
    private void EndDrag(bool save)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _window?.ReleaseMouseCapture();
        if (!save)
        {
            return;
        }

        var center = _center;
        _control.Update(o => o with { Position = center });
        _settings = _control.Settings;

        // The monitor may have another scale than where the drag started.
        Arrange(animate: false);
        Place();
    }
}
