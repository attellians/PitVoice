using System.Collections.Concurrent;
using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PitVoice.Host.Tests.Diagnostics;

/// <summary>Serilog sink that keeps records in memory, so tests see what the host logs without a file.</summary>
internal sealed class LogCapture : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => [.. _events];

    /// <summary>Rendered messages with the level, e.g. <c>WRN Pit commands not applied: ...</c>.</summary>
    public IReadOnlyList<string> Lines => [.. _events.Select(Render)];

    public int Count => _events.Count;

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    /// <summary>A logger that writes here and, when given, to <paramref name="also"/> (e.g. the file log).</summary>
    public Logger CreateLogger(ILogger? also = null)
    {
        var configuration = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this);
        if (also is not null)
        {
            configuration = configuration.WriteTo.Logger(also);
        }

        return configuration.CreateLogger();
    }

    /// <summary>Lines logged after the first <paramref name="skip"/> records.</summary>
    public IReadOnlyList<string> LinesAfter(int skip) => [.. Lines.Skip(skip)];

    private static string Render(LogEvent e)
    {
        var level = e.Level switch
        {
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => "DBG",
        };
        var text = $"{level} {e.RenderMessage(CultureInfo.InvariantCulture)}";
        return e.Exception is null ? text : $"{text} | {e.Exception}";
    }
}
