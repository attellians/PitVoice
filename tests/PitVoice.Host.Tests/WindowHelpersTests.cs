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
    public void Reference_groups_commands_and_marks_own_words()
    {
        var merged = UserAdditions.Merge(
            TestPacks.Ru,
            UserAdditionsLoader.LoadText("lexicon:\n  fuel: { action: [плесни] }\n", "ru.yaml").Additions!);

        var groups = CommandReference.Build(merged.Pack);

        // Spec: No service group.
        Assert.Equal(
            [CommandGroup.Fuel, CommandGroup.Tires, CommandGroup.Windshield, CommandGroup.Repair, CommandGroup.Reset],
            groups.Select(g => g.Group));
        Assert.Equal(TestPacks.Ru.Intents.Count, groups.Sum(g => g.Commands.Count));

        // Spec: Fuel phrases, Own phrase.
        var fuel = groups[0];
        var actions = fuel.Classes.Single(c => c.Class == new WordClass(LexiconGroup.Fuel, WordRole.Action)).Words;
        foreach (var verb in new[] { "залей", "добавь", "заправить", "долей", "налей" })
        {
            Assert.Contains(new ReferenceWord(verb, false), actions);
        }

        Assert.Contains(new ReferenceWord("плесни", true), actions);
        var fuelAdd = fuel.Commands.Single(c => c.Intent == IntentCatalog.FuelAdd);
        Assert.Equal("залей {amount} {unit}", fuelAdd.Examples[0]);
    }

    [Fact]
    public void Reference_keeps_the_pack_order_and_uses_natural_examples()
    {
        var groups = CommandReference.Build(TestPacks.Ru);
        var fuel = groups.Single(g => g.Group == CommandGroup.Fuel);
        var tires = groups.Single(g => g.Group == CommandGroup.Tires);

        Assert.Equal("залей", fuel.Classes[0].Words[0].Text);
        Assert.Equal(["не", "без"], fuel.Classes.Single(c => c.Class is null).Words.Select(w => w.Text));
        Assert.Equal(["переднее левое", "левое переднее"],
            tires.Classes.Single(c => c.Class is { Target: TireTarget.LF }).Words.Select(w => w.Text));
        Assert.Equal("без топлива", fuel.Commands.Single(c => c.Intent == IntentCatalog.FuelNone).Examples[0]);
        Assert.Equal("меняй левые и передние", tires.ElidedAction);

        var en = CommandReference.Build(TestPacks.En).Single(g => g.Group == CommandGroup.Fuel);
        Assert.Equal("no fuel", en.Commands.Single(c => c.Intent == IntentCatalog.FuelNone).Examples[0]);
    }

    [Fact]
    public void Reference_shows_tire_targets_negations_and_the_elided_action()
    {
        var tires = CommandReference.Build(TestPacks.Ru).Single(g => g.Group == CommandGroup.Tires);

        Assert.Contains(tires.Classes, c => c.Class is { Role: WordRole.Action });
        Assert.Equal(9, tires.Classes.Count(c => c.Class is { Role: WordRole.Target }));
        Assert.Contains(tires.Classes, c => c.Class is null && c.Words.Any(w => w.Text == "не"));
        Assert.NotNull(tires.ElidedAction);
        Assert.Contains(" и ", tires.ElidedAction, StringComparison.Ordinal);
        Assert.All(tires.Commands, c => Assert.NotEmpty(c.Examples));
    }

    [Fact]
    public void Reference_examples_parse_into_their_command()
    {
        foreach (var pack in new[] { TestPacks.Ru, TestPacks.En })
        {
            var parser = new Core.Parsing.CommandParser(pack);
            foreach (var command in CommandReference.Build(pack).SelectMany(g => g.Commands))
            {
                Assert.NotEmpty(command.Examples);
                foreach (var example in command.Examples)
                {
                    var typed = example.Replace("{amount}", "20", StringComparison.Ordinal).Replace(" {unit}", string.Empty, StringComparison.Ordinal);
                    var result = Assert.IsType<Core.Parsing.ParseResult.Accepted>(parser.Parse(typed));
                    Assert.Equal(command.Intent, Assert.Single(result.Commands).Intent);
                }
            }
        }
    }

    [Fact]
    public void English_reference_lists_english_words()
    {
        var fuel = CommandReference.Build(TestPacks.En).Single(g => g.Group == CommandGroup.Fuel);
        var actions = fuel.Classes.Single(c => c.Class == new WordClass(LexiconGroup.Fuel, WordRole.Action)).Words.Select(w => w.Text);

        Assert.Contains("add", actions);
        Assert.Contains("fill", actions);
        Assert.Contains("give me", actions);
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
            Assert.Contains("lexicon:", File.ReadAllText(path), StringComparison.Ordinal);

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

        public IReadOnlyList<Core.Audio.AudioDevice> GetInputDevices() => inner.GetInputDevices();

        public IReadOnlyList<Core.Audio.AudioDevice> GetOutputDevices() => inner.GetOutputDevices();

        public IReadOnlyList<Input.GameDevice> GetGameDevices(out string? error) => inner.GetGameDevices(out error);

        public IAudioCuePlayer CreateCuePlayer(string? deviceName, int volume) =>
            new Core.Tests.Pipeline.FakeCuePlayer { Error = "device is in use" };

        public Core.Asr.IAsrEngine CreateEngine() => inner.CreateEngine();

        public Core.Ptt.IPttSource CreatePttSource(Core.Ptt.PttBinding binding) => inner.CreatePttSource(binding);

        public Core.Ptt.IPttSource CreateAssignmentSource() => inner.CreateAssignmentSource();

        public IRacing.Diagnostics.GameElevation CheckElevation() => inner.CheckElevation();
    }
}
