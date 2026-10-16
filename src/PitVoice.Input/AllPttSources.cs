using System.Runtime.Versioning;
using PitVoice.Core.Ptt;

namespace PitVoice.Input;

/// <summary>
/// Every game device and the keyboard as one source, for button assignment and <c>ptt watch</c>. Opening every device
/// costs CPU, so the pipeline host never keeps this source open (TZ §12). <see cref="Start"/> succeeds when at least one
/// part works; the problems of the others are in <see cref="Warnings"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AllPttSources : IPttSource
{
    private readonly IPttSource[] _sources = [new SdlJoystickSource(), new KeyboardHookSource()];
    private readonly List<string> _warnings = [];

    public AllPttSources()
    {
        foreach (var source in _sources)
        {
            source.Input += input => Input?.Invoke(input);
            source.DeviceChanged += change => DeviceChanged?.Invoke(change);
        }
    }

    public event Action<PttInput>? Input;

    public event Action<PttDeviceChange>? DeviceChanged;

    /// <summary>Why a part of the source does not work (no game devices, keyboard hook failed).</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    public string? Start()
    {
        foreach (var source in _sources)
        {
            if (source.Start() is { } error)
            {
                _warnings.Add(error);
            }
        }

        return _warnings.Count == _sources.Length ? string.Join("; ", _warnings) : null;
    }

    public void Dispose()
    {
        foreach (var source in _sources)
        {
            source.Dispose();
        }
    }
}
