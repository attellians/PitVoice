using System.Runtime.Versioning;
using PitVoice.Core.Ptt;
using PitVoice.Input;

namespace PitVoice.Cli;

/// <summary><c>ptt bind</c> and <c>ptt watch</c>: assign the PTT control and see raw presses of every device.</summary>
internal static class PttRunner
{
    public static async Task<int> BindAsync(string? settingsPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("ptt bind needs Windows.");
            return 1;
        }

        var store = Settings.Open(settingsPath);
        var settings = Settings.Load(store);
        Console.WriteLine($"current PTT: {settings.PttBinding?.Describe() ?? "not assigned"}");

        using var assignment = new PttAssignment();
        using var sources = StartAll(assignment.OnInput, change: null);
        Console.WriteLine($"Press the wheel button or the key to use as PTT ({PttAssignment.Timeout.TotalSeconds:0} s, Esc cancels)...");

        PttAssignmentResult result;
        try
        {
            result = await assignment.Result.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Cancelled, PTT unchanged.");
            return 1;
        }

        switch (result.Status)
        {
            case PttAssignmentStatus.Assigned:
                store.Save(settings with { PttBinding = result.Binding });
                Console.WriteLine($"PTT: {result.Binding!.Describe()}");
                if (result.Binding.Kind == PttDeviceKind.Joystick)
                {
                    Console.WriteLine($"device id: {result.Binding.Key.Device}");
                }

                Console.WriteLine($"saved to {store.Path}");
                return 0;

            case PttAssignmentStatus.Cancelled:
                Console.WriteLine($"Cancelled, PTT unchanged: {settings.PttBinding?.Describe() ?? "not assigned"}");
                return 1;

            default:
                Console.WriteLine($"No button pressed: PTT not assigned. It stays: {settings.PttBinding?.Describe() ?? "not assigned"}");
                return 1;
        }
    }

    public static async Task<int> WatchAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("ptt watch needs Windows.");
            return 1;
        }

        using var sources = StartAll(
            input => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {(input.Down ? "down" : "up  ")}  {input.Describe()}  [{input.Key}]"),
            change => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {(change.Connected ? "connected   " : "disconnected")}  {change.Name}  [{change.Device}]"));
        Console.WriteLine("Showing presses of every game device and key, also when another window has the focus. Ctrl+C stops.");
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static AllPttSources StartAll(Action<PttInput> input, Action<PttDeviceChange>? change)
    {
        var sources = new AllPttSources();
        sources.Input += input;
        if (change is not null)
        {
            sources.DeviceChanged += change;
        }

        sources.Start();
        foreach (var warning in sources.Warnings)
        {
            Console.Error.WriteLine($"warning: {warning}");
        }

        return sources;
    }
}
