using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>One pipeline host per user (design decision 4, spec: Single pipeline host).</summary>
public sealed class HostLockTests
{
    private static string UniqueName() => @"Local\PitVoice.Test." + Guid.NewGuid().ToString("N");

    [WindowsFact]
    public void Second_holder_is_refused_until_the_first_releases()
    {
        var name = UniqueName();
        using var first = HostLock.TryAcquire(name);
        Assert.NotNull(first);

        HostLock? second = null;
        var thread = new Thread(() => second = HostLock.TryAcquire(name));
        thread.Start();
        thread.Join();
        Assert.Null(second);

        first.Dispose();
        using var third = HostLock.TryAcquire(name);
        Assert.NotNull(third);
    }

    [WindowsFact]
    public void Release_works_from_any_thread()
    {
        var name = UniqueName();
        var holder = HostLock.TryAcquire(name)!;

        Task.Run(holder.Dispose).Wait(HostFixture.Wait);

        using var next = HostLock.TryAcquire(name);
        Assert.NotNull(next);
    }

    [WindowsFact]
    public void Abandoned_mutex_counts_as_taken()
    {
        var name = UniqueName();
        var thread = new Thread(() =>
        {
            var mutex = new Mutex(initiallyOwned: false, name);
            mutex.WaitOne();
            GC.KeepAlive(mutex); // the thread ends without releasing: the mutex is abandoned
        });
        thread.Start();
        thread.Join();

        using var holder = HostLock.TryAcquire(name);

        Assert.NotNull(holder);
    }

    [Fact]
    public async Task Host_refuses_to_start_while_the_lock_is_held()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var other = HostLock.TryAcquire(HostLock.PipelineHostName);
        if (other is null)
        {
            return; // a real PitVoice runs on this machine; the refusal is then covered by the manual check
        }

        await using var fixture = new HostFixture();
        var host = new AppHost(
            new AppHostOptions { PacksDirectory = Core.Tests.TestPacks.Directory, Models = fixture.Models },
            fixture.Store,
            fixture.Platform,
            fixture.Time);
        await using (host)
        {
            Assert.Equal(HostStartResult.AlreadyRunning, await host.StartAsync());
        }
    }
}

/// <summary>A fact that runs only on Windows (named kernel objects behave differently elsewhere).</summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only";
        }
    }
}
