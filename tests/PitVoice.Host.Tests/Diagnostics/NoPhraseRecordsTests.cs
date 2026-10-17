using System.Text.RegularExpressions;
using PitVoice.Core.Events;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Host.Diagnostics;
using Xunit;

namespace PitVoice.Host.Tests.Diagnostics;

/// <summary>
/// Spec: No phrase records. Phrases with a word and numbers found nowhere else go through the host with every outcome
/// and through the "Check" page; afterwards the log directory holds only log files, none of them mentions the phrases,
/// and nothing was logged for an applied or a rejected phrase.
/// </summary>
public sealed partial class NoPhraseRecordsTests
{
    private const string Word = "зюзюка";

    [Fact]
    public async Task Phrases_and_checks_leave_no_trace_in_the_log()
    {
        var capture = new LogCapture();
        await using var fixture = new HostFixture();
        var directory = Path.Combine(fixture.Root, "logs");
        using var file = DiagnosticLog.Create(directory);
        using var logger = capture.CreateLogger(file);
        fixture.Log = logger;
        fixture.Platform.ExecutionLog = new SerilogExecutionLog(logger);
        fixture.Platform.Game.Ignored.Add(PitCommand.FR);
        var host = await fixture.StartAsync();
        var before = capture.Count;

        // Applied.
        fixture.Say("залей тридцать семь литров");
        var applied = await fixture.WaitForAsync<ResultSignaled>();
        Assert.Contains(fixture.Events, e => e is CommandsApplied && e.PhraseId == applied.PhraseId);

        // Rejected: an unknown word.
        fixture.Say($"какая {Word} погода");
        var rejected = await fixture.WaitForAsync<ResultSignaled>(e => e.PhraseId > applied.PhraseId);
        Assert.Contains(fixture.Events, e => e is CommandsRejected && e.PhraseId == rejected.PhraseId);

        // Partially applied: the game ignores fast repair.
        fixture.Say("залей сорок три литра и быстрый ремонт");
        var partial = await fixture.WaitForAsync<ResultSignaled>(e => e.PhraseId > rejected.PhraseId);
        Assert.Contains(fixture.Events, e => e is ApplyFailed { Outcome: ExecutionOutcome.PartiallyApplied } && e.PhraseId == partial.PhraseId);

        // Recognizer failure.
        fixture.Platform.Script.Throw("engine crashed");
        fixture.PressAndRelease();
        var failed = await fixture.WaitForAsync<ResultSignaled>(e => e.PhraseId > partial.PhraseId);

        // The "Check" page: typed and spoken.
        using (var check = new CommandCheck(host))
        {
            Assert.True(check.CheckText("залей тридцать семь литров").Accepted);
            check.CheckText($"{Word} сорок три");
            fixture.Platform.Script.Say(RecognizedPhrase.FromText("залей сорок три литра"));
            var pending = check.StartMicrophone();
            check.StopMicrophone();
            Assert.True((await pending.WaitAsync(HostFixture.Wait))!.Accepted);
        }

        // The checklist flag is applied (and logged) once the pipeline is free, not when the check returns.
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);
        await host.Events.DrainAsync();
        var afterPhrases = capture.LinesAfter(before);
        await fixture.Host.DisposeAsync();
        file.Dispose();

        // Only the failures are there: the partial outcome (without commands) and the stage failure; the first check marks
        // the setup checklist item done, which is a settings change.
        Assert.Collection(
            afterPhrases,
            l => Assert.StartsWith("WRN Pit commands \"partially applied\": ", l, StringComparison.Ordinal),
            l => Assert.Equal($"ERR Stage Recognition failed, phrase {failed.PhraseId}: \"recognition failed: engine crashed\"", l),
            l => Assert.Contains("setup check done True", l, StringComparison.Ordinal));

        var files = Directory.GetFiles(directory);
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.Matches(@"^pitvoice-\d{8}(_\d{3})?\.log$", Path.GetFileName(f)));
        var text = string.Join('\n', files.SelectMany(File.ReadAllLines).Select(StripTimestamp));
        Assert.Contains("Pit commands partially applied", text, StringComparison.Ordinal);
        foreach (var word in new[] { Word, "залей", "тридцать", "семь", "сорок", "три", "литр", "ремонт" })
        {
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch(@"\b(37|43)\b", text);
    }

    /// <summary>The time at the start of a line may contain any digits.</summary>
    private static string StripTimestamp(string line) => Timestamp().Replace(line, string.Empty);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} ")]
    private static partial Regex Timestamp();
}
