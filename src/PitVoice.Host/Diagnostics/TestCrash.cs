using System.Runtime.CompilerServices;
using Serilog;

namespace PitVoice.Host.Diagnostics;

/// <summary>Where a test crash is thrown.</summary>
public enum TestCrashKind
{
    /// <summary>In the window thread (the app only).</summary>
    Ui,

    /// <summary>In a new background thread.</summary>
    Background,

    /// <summary>In a task nobody waits for; reported as not fatal.</summary>
    Task,
}

/// <summary>
/// The manual check "forced crash → archive" (design decision 5, spec: Test crash): with
/// <see cref="EnvironmentVariable"/> set to <c>ui</c>, <c>background</c> or <c>task</c>, an unhandled exception is thrown
/// <see cref="Delay"/> after start. There is nothing about it in the interface.
/// </summary>
public static class TestCrash
{
    public const string EnvironmentVariable = "PITVOICE_TEST_CRASH";

    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);

    private static ITimer? s_timer;

    /// <summary>The kind for <paramref name="value"/>, or null with a warning for the log when it is unknown or not possible here.</summary>
    /// <param name="value">Value of the variable; null or empty means no test crash and no warning.</param>
    /// <param name="hasWindow">False in <c>run</c>, where <c>ui</c> is not possible.</param>
    /// <param name="warning">Why the value is ignored, or null.</param>
    public static TestCrashKind? Parse(string? value, bool hasWindow, out string? warning)
    {
        warning = null;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        TestCrashKind? kind = text.ToUpperInvariant() switch
        {
            "UI" => TestCrashKind.Ui,
            "BACKGROUND" => TestCrashKind.Background,
            "TASK" => TestCrashKind.Task,
            _ => null,
        };
        if (kind is null)
        {
            warning = $"{EnvironmentVariable}={text} is unknown and ignored; use ui, background or task";
        }
        else if (kind == TestCrashKind.Ui && !hasWindow)
        {
            warning = $"{EnvironmentVariable}=ui needs the window and is ignored here; use background or task";
            kind = null;
        }

        return kind;
    }

    /// <summary>Reads <see cref="EnvironmentVariable"/> and, when set, throws the test exception after <see cref="Delay"/>.</summary>
    /// <param name="log">Gets the warning about an unknown value, or the note that a test crash is coming.</param>
    /// <param name="runOnWindow">Runs an action on the window thread; null in <c>run</c>.</param>
    /// <param name="time">Clock for the delay.</param>
    public static void Schedule(ILogger log, Action<Action>? runOnWindow, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        var kind = Parse(Environment.GetEnvironmentVariable(EnvironmentVariable), runOnWindow is not null, out var warning);
        if (warning is not null)
        {
            log.Warning("{Warning}", warning);
        }

        if (kind is not { } crash)
        {
            return;
        }

        log.Warning("Test crash ({Kind}) in {Seconds} s", crash.ToString().ToLowerInvariant(), Delay.TotalSeconds);
        s_timer = (time ?? TimeProvider.System).CreateTimer(_ => Throw(crash, runOnWindow), null, Delay, Timeout.InfiniteTimeSpan);
    }

    private static void Throw(TestCrashKind kind, Action<Action>? runOnWindow)
    {
        var exception = new InvalidOperationException($"Test crash requested by {EnvironmentVariable}={kind.ToString().ToLowerInvariant()}");
        switch (kind)
        {
            case TestCrashKind.Ui:
                runOnWindow!(() => throw exception);
                break;
            case TestCrashKind.Background:
                new Thread(() => throw exception) { IsBackground = true, Name = "PitVoice test crash" }.Start();
                break;
            case TestCrashKind.Task:
                StartFaultedTask(exception);
                // The unobserved exception is reported when the task is collected; make that happen now.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                break;
        }
    }

    /// <summary>A separate frame, so no local keeps the task alive for the collection.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartFaultedTask(Exception exception)
    {
        var task = System.Threading.Tasks.Task.Run(new Action(() => throw exception));
        ((IAsyncResult)task).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));
    }
}
