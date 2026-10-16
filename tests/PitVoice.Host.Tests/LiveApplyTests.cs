using PitVoice.Core;
using PitVoice.Core.Cues;
using PitVoice.Core.Events;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>The session and live apply of settings (design decision 2, spec: Live apply, Language switch).</summary>
public sealed class LiveApplyTests
{
    [Fact]
    public async Task Phrase_goes_from_the_button_to_the_game()
    {
        await using var fixture = new HostFixture();
        await fixture.StartAsync();

        fixture.Say("залей тридцать литров");

        var applied = await fixture.WaitForAsync<CommandsApplied>();
        Assert.Contains(applied.Sent, s => s.Command == PitCommand.Fuel && s.Parameter == 30);
        var signaled = await fixture.WaitForAsync<ResultSignaled>();
        Assert.Equal(Cue.Success, signaled.Cue);
        Assert.Equal([Cue.Start, Cue.Success], fixture.Platform.LastCues.Played);
    }

    [Fact]
    public async Task Change_microphone_applies_to_the_next_press()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();

        Assert.True(host.Update(s => s with { Microphone = "USB Mic" }).Saved);
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);
        fixture.Say("залей тридцать литров");
        await fixture.WaitForAsync<ResultSignaled>();

        Assert.Equal(["USB Mic"], fixture.Platform.PipelineCapture.Devices);
        Assert.Equal("USB Mic", fixture.Store.Current.Microphone);
        Assert.Single(fixture.Platform.Engines); // the recognizer is kept
    }

    [Fact]
    public async Task Change_mode_during_recording_finishes_the_recording_in_the_old_mode()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        fixture.Platform.Script.Say("залей тридцать литров");

        fixture.Press();
        host.Update(s => s with { PttMode = PttMode.Toggle });
        var applied = host.WhenAppliedAsync();
        await Task.Delay(200);
        Assert.False(applied.IsCompleted); // waits for the phrase

        fixture.Release(); // hold mode: the release stops the recording
        var released = await fixture.WaitForAsync<PttReleased>();
        Assert.Equal(RecordingStopReason.Released, released.Reason);
        await fixture.WaitForAsync<ResultSignaled>();
        await applied.WaitAsync(HostFixture.Wait);

        // Toggle: press and release keep recording, the second press stops.
        fixture.Platform.Script.Say("залей двадцать литров");
        fixture.PressAndRelease();
        await fixture.WaitForAsync<PttPressed>(e => e.PhraseId == 2, pumpTime: false);
        Assert.DoesNotContain(fixture.Events, e => e is PttReleased { PhraseId: 2 });
        fixture.Time.Advance(PttStateMachine.ToggleDebounce);
        fixture.Press();
        var second = await fixture.WaitForAsync<PttReleased>(e => e.PhraseId == 2, pumpTime: false);
        Assert.Equal(RecordingStopReason.SecondPress, second.Reason);
    }

    [Fact]
    public async Task Switch_to_a_language_without_model_gives_no_model_and_the_error_cue()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();

        host.Update(s => s with { Language = "en" });
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);

        Assert.Equal(HostState.NoModel, host.Status.State);
        Assert.Equal(StatusReason.ModelNotInstalled, host.Status.Reason);
        Assert.Equal(HostFixture.EnModel, host.Status.Detail);
        Assert.Equal("en", host.Pack!.Language);

        fixture.PressAndRelease();
        var unavailable = await fixture.WaitForAsync<PttUnavailable>();
        Assert.Equal(StatusReason.ModelNotInstalled, unavailable.Reason);
        Assert.Equal([Cue.Error], fixture.Platform.LastCues.Played);
        Assert.Equal(0, fixture.Platform.Script.Calls);
    }

    [Fact]
    public async Task Press_while_the_model_loads_gets_the_error_cue_with_model_loading()
    {
        await using var fixture = new HostFixture(installedModels: [HostFixture.RuModel, HostFixture.EnModel]);
        var host = await fixture.StartAsync();
        fixture.Platform.LoadGate = new TaskCompletionSource();

        host.Update(s => s with { Language = "en" });
        var applied = host.WhenAppliedAsync();
        await WaitUntilAsync(() => host.Status.Reason == StatusReason.ModelLoading);
        Assert.Equal(HostState.NoModel, host.Status.State);

        fixture.PressAndRelease();
        var unavailable = await fixture.WaitForAsync<PttUnavailable>();
        Assert.Equal(StatusReason.ModelLoading, unavailable.Reason);

        fixture.Platform.LoadGate.SetResult();
        await applied.WaitAsync(HostFixture.Wait);
        Assert.NotEqual(HostState.NoModel, host.Status.State);
        fixture.Say("change all tires");
        var parsed = await fixture.WaitForAsync<CommandsParsed>();
        Assert.Equal("tires.all", Assert.Single(parsed.Commands).Intent.Value);
        Assert.True(fixture.Platform.Engines.First().Disposed); // the ru model is unloaded
    }

    [Fact]
    public async Task Several_changes_during_a_phrase_are_applied_once()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        var gate = new SemaphoreSlim(0);
        fixture.Platform.Script.Say("залей тридцать литров", gate);

        fixture.PressAndRelease();
        await fixture.WaitForAsync<AudioCaptured>(pumpTime: false);
        host.Update(s => s with { CueVolume = 10 });
        host.Update(s => s with { CueVolume = 20 });
        host.Update(s => s with { CueVolume = 30, DefaultUnit = FuelUnits.Gallons });
        await Task.Delay(200);
        Assert.Single(fixture.Platform.CuePlayers); // nothing applied while the phrase is processed

        gate.Release();
        await fixture.WaitForAsync<ResultSignaled>();
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);

        Assert.Equal(2, fixture.Platform.CuePlayers.Count);
        Assert.Equal(30, fixture.Platform.CuePlayers.Last().Volume);
        Assert.Equal(3, fixture.Store.Saved.Count); // every change is saved at once
    }

    [Fact]
    public async Task Interface_language_and_theme_do_not_rebuild_the_session()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        var players = fixture.Platform.CuePlayers.Count;

        host.Update(s => s with { InterfaceLanguage = "en", Theme = Core.Settings.AppTheme.Dark });
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);

        Assert.Equal(players, fixture.Platform.CuePlayers.Count);
        Assert.Single(fixture.Platform.Engines);
        Assert.Equal("en", fixture.Store.Current.InterfaceLanguage);
    }

    [Fact]
    public async Task Read_only_settings_file_reports_the_error_and_the_change_still_applies()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        fixture.Store.ReadOnly = true;

        var result = host.Update(s => s with { DefaultUnit = FuelUnits.Gallons });
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);

        Assert.False(result.Saved);
        Assert.Contains("denied", result.Error, StringComparison.Ordinal);
        Assert.Equal(FuelUnits.Liters, fixture.Store.Current.DefaultUnit);
        fixture.Say("залей десять");
        var parsed = await fixture.WaitForAsync<CommandsParsed>();
        Assert.Equal(FuelUnits.Gallons, Assert.Single(parsed.Commands).Unit);
    }

    [Fact]
    public async Task Phrase_ids_continue_across_sessions()
    {
        await using var fixture = new HostFixture(installedModels: [HostFixture.RuModel, HostFixture.EnModel]);
        var host = await fixture.StartAsync();
        fixture.Say("залей тридцать литров");
        await fixture.WaitForAsync<ResultSignaled>(e => e.PhraseId == 1);

        host.Update(s => s with { Language = "en" });
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);
        fixture.Say("change all tires");

        await fixture.WaitForAsync<ResultSignaled>(e => e.PhraseId == 2);
    }

    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + HostFixture.Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("condition not met");
            }

            await Task.Delay(10);
        }
    }
}
