using PitVoice.Core.Events;
using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;
using PitVoice.Core.Settings;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>The "Check" page (design decision 10, spec: Text check, Microphone check).</summary>
public sealed class CommandCheckTests
{
    [Fact]
    public async Task Compound_phrase_shows_commands_confidence_and_response_without_sending()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        using var check = new CommandCheck(host);

        var result = check.CheckText("меняй шины и залей двадцать пять литров");

        Assert.True(result.Accepted);
        Assert.Equal([IntentCatalog.TiresAll, IntentCatalog.FuelAdd], result.Commands.Select(c => c.Intent));
        Assert.Equal(25, result.Commands[1].Amount);
        Assert.NotNull(result.Confidence);
        Assert.Contains("25", result.Response, StringComparison.Ordinal);
        Assert.Empty(((Core.Tests.Pit.RecordingPitCommandSink)fixture.Platform.Sink).Sent);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Rejected_phrase_shows_the_not_understood_response()
    {
        await using var fixture = new HostFixture();
        using var check = new CommandCheck(await fixture.StartAsync());

        var result = check.CheckText("какая сегодня погода");

        Assert.False(result.Accepted);
        Assert.IsType<ParseResult.Rejected>(result.Parse);
        Assert.Equal("не понял, повтори", result.Response);
    }

    [Fact]
    public async Task Text_check_uses_the_current_unit()
    {
        await using var fixture = new HostFixture(AppSettings.Defaults("ru") with { DefaultUnit = Core.FuelUnits.Gallons });
        using var check = new CommandCheck(await fixture.StartAsync());

        Assert.Equal(Core.FuelUnits.Gallons, Assert.Single(check.CheckText("залей десять").Commands).Unit);
    }

    [Fact]
    public async Task Text_check_works_without_a_model_and_the_microphone_check_does_not()
    {
        await using var fixture = new HostFixture(installedModels: [HostFixture.EnModel]);
        using var check = new CommandCheck(await fixture.StartAsync());

        Assert.True(check.CheckText("залей тридцать литров").Accepted);
        Assert.False(check.MicrophoneAvailable);
        var mic = await check.StartMicrophone();
        Assert.NotNull(mic!.Error);
        Assert.Empty(fixture.Platform.CheckCaptures);
    }

    [Fact]
    public async Task Spoken_check_shows_words_with_confidence_and_commands_and_sends_nothing()
    {
        await using var fixture = new HostFixture(AppSettings.Defaults("ru") with { PttBinding = HostFixture.KeyBinding, Microphone = "USB Mic" });
        var host = await fixture.StartAsync();
        using var check = new CommandCheck(host);
        fixture.Platform.Script.Say(new RecognizedPhrase([new("залей", 0.97), new("тридцать", 0.91), new("литров", 0.88)]));

        var pending = check.StartMicrophone();
        Assert.Equal(MicCheckState.Recording, check.State);
        check.StopMicrophone();
        var result = await pending.WaitAsync(HostFixture.Wait);

        Assert.Equal("залей тридцать литров", result!.Text);
        Assert.Equal([0.97, 0.91, 0.88], result.Words.Select(w => w.Confidence!.Value));
        Assert.Equal(30, Assert.Single(result.Commands).Amount);
        Assert.Equal(["USB Mic"], fixture.Platform.CheckCaptures.Single().Devices);
        Assert.Empty(fixture.Platform.PipelineCapture.Devices);
        Assert.Empty(((Core.Tests.Pit.RecordingPitCommandSink)fixture.Platform.Sink).Sent);
        Assert.Equal(MicCheckState.Idle, check.State);
    }

    [Fact]
    public async Task Silence_is_no_speech()
    {
        await using var fixture = new HostFixture();
        using var check = new CommandCheck(await fixture.StartAsync());
        fixture.Platform.Script.Say(RecognizedPhrase.Empty);

        var pending = check.StartMicrophone();
        check.StopMicrophone();
        var result = await pending.WaitAsync(HostFixture.Wait);

        Assert.True(result!.NoSpeech);
        Assert.False(result.Accepted);
    }

    [Fact]
    public async Task Microphone_unavailable_shows_the_device_error()
    {
        await using var fixture = new HostFixture();
        fixture.Platform.CheckCaptureError = "no microphone found";
        using var check = new CommandCheck(await fixture.StartAsync());

        var result = await check.StartMicrophone();

        Assert.Equal("no microphone found", result!.Error);
        Assert.Equal(MicCheckState.Idle, check.State);
    }

    [Fact]
    public async Task Recording_stops_by_itself_after_eight_seconds()
    {
        await using var fixture = new HostFixture();
        using var check = new CommandCheck(await fixture.StartAsync());
        fixture.Platform.Script.Say("залей тридцать литров");

        var pending = check.StartMicrophone();
        fixture.Time.Advance(CommandCheck.AutoStop);
        var result = await pending.WaitAsync(HostFixture.Wait);

        Assert.True(result!.Accepted);
    }

    [Fact]
    public async Task Ptt_press_drops_the_check_and_works_as_usual()
    {
        await using var fixture = new HostFixture();
        using var check = new CommandCheck(await fixture.StartAsync());

        var pending = check.StartMicrophone();
        fixture.Press();
        var result = await pending.WaitAsync(HostFixture.Wait);

        Assert.Null(result);
        Assert.Equal(MicCheckState.Idle, check.State);
        Assert.Equal(1, fixture.Platform.CheckCaptures.Single().Stops);
        await fixture.WaitForAsync<PttPressed>(pumpTime: false);
        Assert.Single(fixture.Platform.PipelineCapture.Devices);
    }

    [Fact]
    public async Task First_check_marks_the_checklist_item_done_without_rebuilding_the_session()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        using var check = new CommandCheck(host);
        var players = fixture.Platform.CuePlayers.Count;

        check.CheckText("залей тридцать литров");
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);

        Assert.True(fixture.Store.Current.SetupCheckDone);
        Assert.Equal(players, fixture.Platform.CuePlayers.Count);
    }
}
