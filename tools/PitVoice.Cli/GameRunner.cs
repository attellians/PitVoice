using System.Globalization;
using System.Text;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Responses;
using PitVoice.IRacing.Commands;
using PitVoice.IRacing.Diagnostics;
using PitVoice.IRacing.Recording;
using PitVoice.IRacing.Telemetry;

namespace PitVoice.Cli;

/// <summary><c>status</c>, <c>send</c> and <c>record</c>: the game side of the pipeline.</summary>
internal static class GameRunner
{
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(100);

    public static async Task<int> StatusAsync(bool watch, CancellationToken cancellationToken)
    {
        using var telemetry = Game.OpenTelemetry();
        var snapshot = telemetry.Source.Current;
        Console.WriteLine(Describe(snapshot));
        if (!watch)
        {
            return 0;
        }

        var last = Describe(snapshot);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(WatchInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var text = Describe(telemetry.Source.Current);
            if (text != last)
            {
                Console.WriteLine();
                Console.WriteLine(text);
                last = text;
            }
        }

        return 0;
    }

    public static async Task<int> SendAsync(string directory, string language, string phrase, ParseOptions options, CancellationToken cancellationToken)
    {
        var pack = Packs.LoadLanguage(directory, language, Console.Error);
        if (pack is null)
        {
            return 2;
        }

        var parser = new CommandParser(pack);
        var responses = new ResponseBuilder(pack);
        var parsed = ParseOutput.Print(parser, responses, RecognizedPhrase.FromText(phrase), options, printResponse: false);
        if (parsed is not ParseResult.Accepted accepted)
        {
            Console.WriteLine($"response: {responses.Build(parsed)}");
            return 1;
        }

        using var telemetry = Game.OpenTelemetry();
        var executor = new PitExecutor(telemetry.Source, Game.OpenSink(), TimeProvider.System, Game.Log());
        var result = await executor.ExecuteAsync(accepted, phrase, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"sent:     {(result.Sent.Count == 0 ? "nothing" : string.Join(", ", result.Sent))}");
        Console.WriteLine($"outcome:  {Describe(result.Outcome)}");
        if (result.Unconfirmed.Count > 0)
        {
            Console.WriteLine($"unconfirmed: {string.Join(", ", result.Unconfirmed.Select(c => c.Intent))}");
        }

        Console.WriteLine($"response: {responses.Build(parsed, result)}");
        return result.Outcome == ExecutionOutcome.Applied ? 0 : 1;
    }

    public static async Task<int> RecordAsync(string path, CancellationToken cancellationToken)
    {
        using var telemetry = Game.OpenTelemetry();
        await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var recorder = new TelemetryRecorder(writer);
        Console.WriteLine($"Recording telemetry to {Path.GetFullPath(path)}. Ctrl+C stops.");
        await recorder.RunAsync(
            telemetry.Source,
            TimeProvider.System,
            snapshot => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {Describe(snapshot.State)}"),
            cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Stopped, {recorder.Written} record(s) written.");
        return 0;
    }

    private static string Describe(TelemetrySnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"state:       {Describe(snapshot.State)}");
        if (snapshot.Pit is { } pit)
        {
            sb.AppendLine();
            sb.Append(CultureInfo.InvariantCulture, $"""
                wheels:      {pit.Wheels.Describe()}
                fuel:        {(pit.FuelFill ? "on" : "off")}, {pit.FuelToAdd:0.#} l to add
                tank:        {pit.FuelLevel:0.#} l of {(snapshot.FuelCapacity is { } capacity ? capacity.ToString("0.#", CultureInfo.InvariantCulture) + " l" : "unknown")}
                tearoff:     {(pit.Tearoff ? "on" : "off")}
                fast repair: {(pit.FastRepair ? "on" : "off")}
                flags:       0x{(int)pit.Flags:X2}
                """);
        }

        return sb.ToString();
    }

    internal static string Describe(ConnectionState state) => state switch
    {
        ConnectionState.NotRunning => "game not running",
        ConnectionState.Connected => "game running, driver not in the car",
        ConnectionState.InCar => "driver in the car",
        _ => state.ToString(),
    };

    internal static string Describe(ExecutionOutcome outcome) => outcome switch
    {
        ExecutionOutcome.Applied => "applied",
        ExecutionOutcome.PartiallyApplied => "partially applied",
        ExecutionOutcome.NotApplied => "not applied",
        ExecutionOutcome.NotConnected => "no connection (game not running)",
        ExecutionOutcome.NotInCar => "not in the car",
        ExecutionOutcome.NotSupported => "not supported yet",
        _ => outcome.ToString(),
    };

    /// <summary>The real game on Windows; elsewhere a game that is never running.</summary>
    internal static class Game
    {
        public static TelemetryHandle OpenTelemetry()
        {
            if (OperatingSystem.IsWindows())
            {
                var source = new IRacingTelemetrySource();
                return new TelemetryHandle(source, source);
            }

            return new TelemetryHandle(NoGame.Instance, null);
        }

        public static IPitCommandSink OpenSink() =>
            OperatingSystem.IsWindows() ? new BroadcastPitCommandSink() : NoGame.Instance;

        public static IExecutionLog ConsoleLog => ConsoleExecutionLog.Instance;

        public static IExecutionLog Log() =>
            OperatingSystem.IsWindows()
                ? new ElevationHintLog(ConsoleExecutionLog.Instance, new WindowsGameElevationProbe())
                : ConsoleExecutionLog.Instance;
    }

    internal sealed class TelemetryHandle(ITelemetrySource source, IDisposable? owner) : IDisposable
    {
        public ITelemetrySource Source { get; } = source;

        public void Dispose() => owner?.Dispose();
    }

    private sealed class NoGame : ITelemetrySource, IPitCommandSink
    {
        public static NoGame Instance { get; } = new();

        public TelemetrySnapshot Current => TelemetrySnapshot.NotRunning;

        public void Send(PitCommand command, int parameter) =>
            throw new PlatformNotSupportedException("iRacing pit commands are only available on Windows.");
    }

    private sealed class ConsoleExecutionLog : IExecutionLog
    {
        public static ConsoleExecutionLog Instance { get; } = new();

        public void ApplyFailed(ApplyFailureReport report) => Console.Error.WriteLine($"warn: {report}");

        public void Warning(string message) => Console.Error.WriteLine($"warn: {message}");
    }
}
