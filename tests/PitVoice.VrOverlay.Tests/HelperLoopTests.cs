using System.Collections.Concurrent;
using System.IO.Pipes;
using PitVoice.Core.Vr;
using Xunit;

namespace PitVoice.VrOverlay.Tests;

/// <summary>The helper on a fake OpenVR (add-vr-overlay D3, D4, D6; task 3.3).</summary>
public sealed class HelperLoopTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Frame_visibility_and_place_reach_the_overlay_and_ping_gets_pong()
    {
        var vr = new FakeVr();
        await using var run = new Run(vr);
        var frame = new VrFrame(1, 1, [1, 2, 3, 4]);

        run.Send(new VrPlace(20, 10, 3));
        run.Send(frame);
        run.Send(new VrVisible(true));
        run.Send(new VrPing());

        await run.Line("pong");
        Assert.Equal(["ready", "pong"], run.Lines);
        Assert.Equal(3, vr.Applied.Count);
        Assert.Equal(new VrPlace(20, 10, 3), vr.Applied[0]);
        var applied = Assert.IsType<VrFrame>(vr.Applied[1]);
        Assert.Equal(frame.Rgba, applied.Rgba);
        Assert.Equal(new VrVisible(true), vr.Applied[2]);
        Assert.True(vr.OverlayCreated);
        Assert.Equal(VrExitCode.InputClosed, await run.CloseInput());
        Assert.True(vr.ShutDown);
    }

    [Fact]
    public async Task Closed_input_ends_the_helper_at_once()
    {
        var vr = new FakeVr();
        await using var run = new Run(vr);
        await run.Line("ready");

        Assert.Equal(VrExitCode.InputClosed, await run.CloseInput());
        Assert.True(vr.ShutDown);
        Assert.False(vr.QuitAcknowledged);
    }

    /// <summary>D4: on VREvent_Quit the helper acknowledges, shuts down and exits with "SteamVR closed".</summary>
    [Fact]
    public async Task SteamVR_quit_is_acknowledged_and_ends_the_helper()
    {
        var vr = new FakeVr();
        await using var run = new Run(vr);
        await run.Line("ready");

        vr.Quit = true;

        Assert.Equal(VrExitCode.SteamVrClosed, await run.Exit);
        Assert.Equal(["ready", "quit"], run.Lines);
        Assert.True(vr.QuitAcknowledged);
        Assert.True(vr.ShutDown);
        Assert.True(vr.QuitAcknowledgedBeforeShutdown);
    }

    [Fact]
    public async Task Init_failure_says_error_and_exits_with_its_code()
    {
        var vr = new FakeVr { InitFailure = new VrFailure(VrExitCode.InitFailed, "init-110") };
        await using var run = new Run(vr);

        Assert.Equal(VrExitCode.InitFailed, await run.Exit);
        Assert.Equal(["error init-110"], run.Lines);
        Assert.False(vr.OverlayCreated);
        Assert.True(vr.ShutDown);
    }

    [Fact]
    public async Task Overlay_failure_says_error_and_exits_with_its_code()
    {
        var vr = new FakeVr { ApplyFailure = new VrFailure(VrExitCode.OverlayFailed, "frame-24") };
        await using var run = new Run(vr);

        run.Send(new VrFrame(1, 1, new byte[4]));

        Assert.Equal(VrExitCode.OverlayFailed, await run.Exit);
        Assert.Equal(["ready", "error frame-24"], run.Lines);
    }

    [Fact]
    public async Task Broken_message_is_a_protocol_error()
    {
        var vr = new FakeVr();
        await using var run = new Run(vr);

        run.SendBytes([1, 0, 0, 0, 77]);

        Assert.Equal(VrExitCode.ProtocolError, await run.Exit);
        Assert.Equal(["ready", "error protocol"], run.Lines);
    }

    [Fact]
    public async Task Events_are_read_about_ten_times_a_second()
    {
        var vr = new FakeVr();
        await using var run = new Run(vr);
        await run.Line("ready");

        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.InRange(vr.Polls, 5, 15);
        Assert.Equal(VrExitCode.InputClosed, await run.CloseInput());
    }

    /// <summary>The helper loop on its own thread, with the app side of a real pipe.</summary>
    private sealed class Run : IAsyncDisposable
    {
        private readonly AnonymousPipeServerStream _app = new(PipeDirection.Out);
        private readonly AnonymousPipeClientStream _helper;
        private readonly LineWriter _output = new();

        public Run(FakeVr vr)
        {
            _helper = new AnonymousPipeClientStream(PipeDirection.In, _app.ClientSafePipeHandle);
            Exit = Task.Run(() => HelperLoop.Run(vr, _helper, _output)).WaitAsync(Wait);
        }

        public Task<int> Exit { get; }

        public IReadOnlyList<string> Lines => _output.Lines;

        public void Send(VrMessage message) => VrProtocol.Write(_app, message);

        public void SendBytes(byte[] bytes)
        {
            _app.Write(bytes);
            _app.Flush();
        }

        public async Task Line(string line)
        {
            using var timeout = new CancellationTokenSource(Wait);
            while (!_output.Lines.Contains(line))
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        public Task<int> CloseInput()
        {
            _app.Dispose();
            return Exit;
        }

        public async ValueTask DisposeAsync()
        {
            _app.Dispose();
            try
            {
                await Exit;
            }
            catch (TimeoutException)
            {
            }

            _helper.Dispose();
        }
    }

    private sealed class LineWriter : StringWriter
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Lines => [.. _lines];

        public override void WriteLine(string? value) => _lines.Enqueue(value ?? string.Empty);
    }

    private sealed class FakeVr : IOpenVr
    {
        private readonly ConcurrentQueue<VrMessage> _applied = new();
        private int _polls;

        public VrFailure? InitFailure { get; init; }

        public VrFailure? ApplyFailure { get; init; }

        public volatile bool Quit;

        public bool OverlayCreated { get; private set; }

        public bool QuitAcknowledged { get; private set; }

        public bool QuitAcknowledgedBeforeShutdown { get; private set; }

        public bool ShutDown { get; private set; }

        public int Polls => _polls;

        public IReadOnlyList<VrMessage> Applied => [.. _applied];

        public VrFailure? Init() => InitFailure;

        public VrFailure? CreateOverlay()
        {
            OverlayCreated = true;
            return null;
        }

        public VrFailure? Apply(VrMessage message)
        {
            _applied.Enqueue(message);
            return ApplyFailure;
        }

        public bool PollQuit()
        {
            Interlocked.Increment(ref _polls);
            return Quit;
        }

        public void AcknowledgeQuit()
        {
            QuitAcknowledged = true;
            QuitAcknowledgedBeforeShutdown = !ShutDown;
        }

        public void Shutdown() => ShutDown = true;
    }
}
