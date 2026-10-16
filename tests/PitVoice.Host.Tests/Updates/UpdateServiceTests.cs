using PitVoice.Core.Events;
using PitVoice.Core.Pit;
using PitVoice.Host.Diagnostics;
using PitVoice.Host.Tests.Diagnostics;
using PitVoice.Host.Updates;
using Xunit;

namespace PitVoice.Host.Tests.Updates;

/// <summary>Update checks on a fake release source and virtual time (design decision 4, app-updates spec).</summary>
public sealed class UpdateServiceTests
{
    private static readonly UpdateInfo Newer = new("0.1.3", "https://github.com/attellians/PitVoice/releases/tag/v0.1.3");

    [Fact]
    public async Task Newer_version_found_on_start()
    {
        await using var fixture = new HostFixture();
        var source = new FakeUpdateSource { Latest = Newer };
        await using var updates = Create(fixture, source);

        updates.Start();
        await updates.LastCheck.WaitAsync(HostFixture.Wait);

        Assert.Equal(new UpdateState(UpdateStatus.Available, Newer), updates.State);
        Assert.Equal(1, source.Checks);
    }

    [Fact]
    public async Task Already_up_to_date_gives_one_result_line_and_no_block()
    {
        await using var fixture = new HostFixture();
        var log = new LogCapture();
        var source = new FakeUpdateSource();
        await using var updates = Create(fixture, source, log);

        updates.Start();
        await updates.LastCheck.WaitAsync(HostFixture.Wait);

        Assert.Equal(UpdateStatus.UpToDate, updates.State.Status);
        Assert.Null(updates.State.Update);
        Assert.Single(log.Lines, l => l.Contains("update check:", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains($"{DiagnosticPaths.Version} is the latest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Next_check_runs_after_24_hours()
    {
        await using var fixture = new HostFixture();
        var source = new FakeUpdateSource();
        await using var updates = Create(fixture, source);
        updates.Start();
        await updates.LastCheck.WaitAsync(HostFixture.Wait);

        fixture.Time.Advance(TimeSpan.FromHours(23));
        Assert.Equal(1, source.Checks);

        source.Latest = Newer;
        fixture.Time.Advance(TimeSpan.FromHours(1));
        await updates.LastCheck.WaitAsync(HostFixture.Wait);

        Assert.Equal(2, source.Checks);
        Assert.Equal(UpdateStatus.Available, updates.State.Status);
    }

    [Fact]
    public async Task No_network_during_automatic_check_is_only_logged()
    {
        await using var fixture = new HostFixture();
        var log = new LogCapture();
        var source = new FakeUpdateSource { Failure = new HttpRequestException("No such host is known.") };
        await using var updates = Create(fixture, source, log);
        var changes = new List<UpdateState>();
        updates.Changed += changes.Add;

        updates.Start();
        await updates.LastCheck.WaitAsync(HostFixture.Wait);

        Assert.Equal(UpdateStatus.Idle, updates.State.Status);
        Assert.Empty(changes);
        Assert.Contains(log.Lines, l => l.StartsWith("WRN Automatic update check failed", StringComparison.Ordinal) && l.Contains("No such host", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_does_not_hold_up_a_phrase()
    {
        await using var fixture = new HostFixture();
        await fixture.StartAsync();
        var source = new FakeUpdateSource { Hang = true };
        await using var updates = Create(fixture, source);
        updates.Start();
        await source.CheckStarted.Task.WaitAsync(HostFixture.Wait);

        fixture.Say("залей тридцать литров");

        var applied = await fixture.WaitForAsync<CommandsApplied>();
        Assert.Contains(applied.Sent, s => s.Command == PitCommand.Fuel && s.Parameter == 30);
        Assert.False(updates.LastCheck.IsCompleted);
    }

    [Fact]
    public async Task Check_disabled_makes_no_request_in_48_hours()
    {
        await using var fixture = new HostFixture(Core.Settings.AppSettings.Defaults("ru") with { CheckForUpdates = false });
        await fixture.StartAsync();
        var source = new FakeUpdateSource { Latest = Newer };
        await using var updates = Create(fixture, source);

        updates.Start();
        for (var hour = 0; hour < 48; hour++)
        {
            fixture.Time.Advance(TimeSpan.FromHours(1));
        }

        await updates.LastCheck.WaitAsync(HostFixture.Wait);
        Assert.Equal(0, source.Checks);
        Assert.Equal(UpdateStatus.Idle, updates.State.Status);
    }

    [Fact]
    public async Task Turning_the_setting_off_stops_checks_and_on_checks_at_once()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        var source = new FakeUpdateSource();
        await using var updates = Create(fixture, source);
        updates.Start();
        await updates.LastCheck.WaitAsync(HostFixture.Wait);

        Assert.True(host.Update(s => s with { CheckForUpdates = false }).Saved);
        fixture.Time.Advance(TimeSpan.FromHours(30));
        Assert.Equal(1, source.Checks);
        Assert.False(fixture.Store.Current.CheckForUpdates);

        Assert.True(host.Update(s => s with { CheckForUpdates = true }).Saved);
        await WaitForAsync(() => source.Checks == 2);
        await updates.LastCheck.WaitAsync(HostFixture.Wait);
    }

    [Fact]
    public async Task Manual_check_finds_an_update_with_automatic_checks_off()
    {
        await using var fixture = new HostFixture(Core.Settings.AppSettings.Defaults("ru") with { CheckForUpdates = false });
        await fixture.StartAsync();
        var source = new FakeUpdateSource { Latest = Newer };
        await using var updates = Create(fixture, source);
        updates.Start();

        var state = await updates.CheckNowAsync().WaitAsync(HostFixture.Wait);

        Assert.Equal(new UpdateState(UpdateStatus.Available, Newer), state);
        Assert.Equal(1, source.Checks);
    }

    [Fact]
    public async Task Manual_check_without_network_shows_the_reason()
    {
        await using var fixture = new HostFixture();
        var source = new FakeUpdateSource { Failure = new HttpRequestException("No such host is known.") };
        await using var updates = Create(fixture, source);
        var seen = new List<UpdateStatus>();
        updates.Changed += s => seen.Add(s.Status);

        var state = await updates.CheckNowAsync().WaitAsync(HostFixture.Wait);

        Assert.Equal(UpdateStatus.Failed, state.Status);
        Assert.Equal("No such host is known.", state.Error);
        Assert.Equal([UpdateStatus.Checking, UpdateStatus.Failed], seen);

        source.Failure = null;
        Assert.Equal(UpdateStatus.UpToDate, (await updates.CheckNowAsync().WaitAsync(HostFixture.Wait)).Status); // the button works again
    }

    [Fact]
    public async Task Update_and_restart_downloads_with_progress_then_stops_the_host_and_applies()
    {
        await using var fixture = new HostFixture();
        var log = new LogCapture();
        var source = new FakeUpdateSource { Latest = Newer, Progress = [10, 55, 100] };
        var steps = new List<string>();
        source.Applied += () => steps.Add("apply");
        await using var updates = new UpdateService(source, fixture.Host, () => { steps.Add("stop"); return Task.CompletedTask; }, log.CreateLogger());
        var percents = new List<int>();
        updates.Changed += s =>
        {
            if (s.Status == UpdateStatus.Downloading)
            {
                percents.Add(s.Percent);
            }
        };
        await updates.CheckNowAsync();

        await updates.UpdateAndRestartAsync().WaitAsync(HostFixture.Wait);

        Assert.Equal([0, 10, 55, 100], percents);
        Assert.Equal(["stop", "apply"], steps);
        Assert.Equal(Newer, source.AppliedUpdate);
        var lines = log.Lines;
        Assert.Contains(lines, l => l.Contains("version 0.1.3 found", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Update 0.1.3 download started", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Update 0.1.3 downloaded", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("?", StringComparison.Ordinal)); // no addresses with parameters
    }

    [Fact]
    public async Task Download_fails_offers_the_update_again()
    {
        await using var fixture = new HostFixture();
        var source = new FakeUpdateSource { Latest = Newer, DownloadFailure = new IOException("The connection was reset.") };
        var stopped = false;
        await using var updates = new UpdateService(source, fixture.Host, () => { stopped = true; return Task.CompletedTask; });
        await updates.CheckNowAsync();

        await updates.UpdateAndRestartAsync().WaitAsync(HostFixture.Wait);

        Assert.Equal(new UpdateState(UpdateStatus.Available, Newer, Error: "The connection was reset."), updates.State);
        Assert.False(stopped);
        Assert.Null(source.AppliedUpdate);
    }

    [Fact]
    public async Task Developer_build_never_calls_the_source()
    {
        await using var fixture = new HostFixture();
        var log = new LogCapture();
        var source = new FakeUpdateSource { Installed = false, Latest = Newer };
        await using var updates = Create(fixture, source, log);

        updates.Start();
        fixture.Time.Advance(TimeSpan.FromHours(48));
        var manual = await updates.CheckNowAsync();

        Assert.Equal(UpdateStatus.NotInstalled, manual.Status);
        Assert.False(updates.IsInstalled);
        Assert.Equal(0, source.Checks);
        Assert.Contains(log.Lines, l => l.Contains("not an installed app", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Log_unavailable_does_not_change_checks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pitvoice-updates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var blocker = Path.Combine(root, "logs");
        await File.WriteAllTextAsync(blocker, "a file where the directory should be");
        try
        {
            await using var fixture = new HostFixture();
            using var log = DiagnosticLog.Create(blocker);
            var source = new FakeUpdateSource { Latest = Newer };
            await using var updates = new UpdateService(source, fixture.Host, () => Task.CompletedTask, log);

            updates.Start();
            await updates.LastCheck.WaitAsync(HostFixture.Wait);

            Assert.Equal(UpdateStatus.Available, updates.State.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static UpdateService Create(HostFixture fixture, FakeUpdateSource source, LogCapture? log = null) =>
        new(source, fixture.Host, () => Task.CompletedTask, log?.CreateLogger());

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + HostFixture.Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met in time");
            await Task.Delay(10);
        }
    }

    private sealed class FakeUpdateSource : IUpdateSource
    {
        private int _checks;

        public event Action? Applied;

        public bool Installed { get; init; } = true;

        public UpdateInfo? Latest { get; set; }

        public Exception? Failure { get; set; }

        public Exception? DownloadFailure { get; init; }

        public bool Hang { get; init; }

        public IReadOnlyList<int> Progress { get; init; } = [100];

        public int Checks => Volatile.Read(ref _checks);

        public UpdateInfo? AppliedUpdate { get; private set; }

        public TaskCompletionSource CheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsInstalled => Installed;

        public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _checks);
            CheckStarted.TrySetResult();
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return Failure is { } failure ? throw failure : Latest;
        }

        public Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellationToken)
        {
            foreach (var percent in Progress)
            {
                progress(percent);
            }

            return DownloadFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }

        public void ApplyAndRestart(UpdateInfo update)
        {
            AppliedUpdate = update;
            Applied?.Invoke();
        }
    }
}
