using PitVoice.Core.Cues;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Settings;
using PitVoice.Core.Tests;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>Cue preview, command reference and the additions template (spec: Cue preview, Command reference).</summary>
public sealed class WindowHelpersTests
{
    [Fact]
    public async Task Preview_plays_three_cues_with_the_current_device_and_volume()
    {
        var platform = new FakePlatform(TimeProvider.System);

        var result = await CuePreview.PlayAsync(platform, AppSettings.Defaults("ru") with { OutputDevice = "Headphones", CueVolume = 40 });

        Assert.Equal(CuePreviewOutcome.Played, result.Outcome);
        var (device, volume, player) = Assert.Single(platform.CuePlayers);
        Assert.Equal(("Headphones", 40), (device, volume));
        Assert.Equal([Cue.Start, Cue.Success, Cue.Error], player.Played);
    }

    [Fact]
    public async Task Preview_at_zero_volume_says_the_cues_are_off()
    {
        var platform = new FakePlatform(TimeProvider.System);

        var result = await CuePreview.PlayAsync(platform, AppSettings.Defaults("ru") with { CueVolume = 0 });

        Assert.Equal(CuePreviewOutcome.Muted, result.Outcome);
        Assert.Empty(platform.CuePlayers);
    }

    [Fact]
    public async Task Preview_reports_a_failing_device()
    {
        var result = await CuePreview.PlayAsync(new FailingCuesPlatform(new FakePlatform(TimeProvider.System)), AppSettings.Defaults("ru"));

        Assert.Equal(CuePreviewOutcome.Failed, result.Outcome);
        Assert.Equal("device is in use", result.Error);
    }

    [Fact]
    public void Reference_groups_commands_and_marks_own_phrases()
    {
        var merged = UserAdditions.Merge(
            TestPacks.Ru,
            UserAdditionsLoader.LoadText("language: ru\nintents:\n  fuel.add:\n    patterns: [\"плесни {amount} {unit?}\"]\n", "ru.yaml").Additions!);

        var groups = CommandReference.Build(merged.Pack);

        Assert.Equal(
            [CommandGroup.Fuel, CommandGroup.Tires, CommandGroup.Windshield, CommandGroup.Repair, CommandGroup.Reset, CommandGroup.Service],
            groups.Select(g => g.Group));
        var fuelAdd = groups[0].Commands.Single(c => c.Intent == IntentCatalog.FuelAdd);
        Assert.Contains(new ReferencePhrase("залей {amount} {unit?}", false), fuelAdd.Phrases);
        Assert.Contains(new ReferencePhrase("плесни {amount} {unit?}", true), fuelAdd.Phrases);
        Assert.Equal(TestPacks.Ru.Intents.Count, groups.Sum(g => g.Commands.Count));
    }

    [Fact]
    public void Reference_leaves_out_intents_the_pack_does_not_describe()
    {
        var pack = LanguagePackLoader.LoadFile(Path.Combine(TestPacks.FixturesDirectory, "valid-minimal.yaml")).Pack!;

        var commands = CommandReference.Build(pack).SelectMany(g => g.Commands).Select(c => c.Intent);

        Assert.Equal([IntentCatalog.FuelAdd, IntentCatalog.TiresAll], commands);
    }

    [Fact]
    public void Template_file_adds_nothing_and_is_not_overwritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pitvoice-template-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = UserAdditionsLoader.CreateTemplate(directory, "ru");

            var result = UserAdditions.Apply(TestPacks.Ru, directory);
            Assert.True(result.Applied, string.Join('\n', result.Issues));
            Assert.Empty(result.Issues);
            Assert.Contains("{amount}", File.ReadAllText(path), StringComparison.Ordinal);

            File.WriteAllText(path, "language: ru\nintents: {}\n");
            Assert.Equal(path, UserAdditionsLoader.CreateTemplate(directory, "ru"));
            Assert.Equal("language: ru\nintents: {}\n", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FailingCuesPlatform(FakePlatform inner) : IHostPlatform
    {
        public Core.Audio.IAudioCapture Capture => inner.Capture;

        public Core.Pit.ITelemetrySource Telemetry => inner.Telemetry;

        public Core.Pit.IPitCommandSink Sink => inner.Sink;

        public Core.Pit.IExecutionLog ExecutionLog => inner.ExecutionLog;

        public Core.Audio.IAudioCapture CreateCheckCapture() => inner.CreateCheckCapture();

        public IReadOnlyList<Core.Audio.AudioDevice> GetInputDevices() => inner.GetInputDevices();

        public IReadOnlyList<Core.Audio.AudioDevice> GetOutputDevices() => inner.GetOutputDevices();

        public IAudioCuePlayer CreateCuePlayer(string? deviceName, int volume) =>
            new Core.Tests.Pipeline.FakeCuePlayer { Error = "device is in use" };

        public Core.Asr.IAsrEngine CreateEngine() => inner.CreateEngine();

        public Core.Ptt.IPttSource CreatePttSource(Core.Ptt.PttBinding binding) => inner.CreatePttSource(binding);

        public Core.Ptt.IPttSource CreateAssignmentSource() => inner.CreateAssignmentSource();

        public IRacing.Diagnostics.GameElevation CheckElevation() => inner.CheckElevation();
    }
}
