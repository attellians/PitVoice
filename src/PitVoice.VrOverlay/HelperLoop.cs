using System.Collections.Concurrent;
using System.Diagnostics;
using PitVoice.Core.Vr;

namespace PitVoice.VrOverlay;

/// <summary>
/// The helper (add-vr-overlay D3, D4, D6): connects to SteamVR, creates the overlay, says <c>ready</c>, then applies the
/// messages of the app as they come and reads the events of SteamVR 10 times a second. It ends when the app closes the
/// input, when SteamVR closes or on the first failure, with the matching <see cref="VrExitCode"/>.
/// </summary>
internal static class HelperLoop
{
    /// <summary>How often the events of SteamVR are read (D4).</summary>
    public static readonly TimeSpan EventInterval = TimeSpan.FromMilliseconds(100);

    public static int Run(IOpenVr vr, Stream input, TextWriter output)
    {
        var failure = vr.Init();
        if (failure is not null)
        {
            vr.Shutdown();
            return Fail(output, failure);
        }

        try
        {
            failure = vr.CreateOverlay();
            if (failure is not null)
            {
                return Fail(output, failure);
            }

            Say(output, VrReply.Ready);
            using var queue = new BlockingCollection<object>();
            var reader = new Thread(() => Read(input, queue)) { IsBackground = true, Name = "app input" };
            reader.Start();

            var clock = Stopwatch.StartNew();
            var nextEvents = EventInterval;
            while (true)
            {
                var wait = nextEvents - clock.Elapsed;
                if (queue.TryTake(out var item, wait > TimeSpan.Zero ? wait : TimeSpan.Zero))
                {
                    switch (item)
                    {
                        case EndOfInput:
                            return VrExitCode.InputClosed;
                        case VrProtocolException error:
                            return Fail(output, new VrFailure(VrExitCode.ProtocolError, "protocol"), error.Message);
                        case VrPing:
                            Say(output, VrReply.Pong);
                            break;
                        case VrMessage message when vr.Apply(message) is { } applyFailure:
                            return Fail(output, applyFailure);
                    }
                }

                if (clock.Elapsed < nextEvents)
                {
                    continue;
                }

                nextEvents = clock.Elapsed + EventInterval;
                if (vr.PollQuit())
                {
                    Say(output, VrReply.Quit);
                    vr.AcknowledgeQuit();
                    return VrExitCode.SteamVrClosed;
                }
            }
        }
        finally
        {
            vr.Shutdown();
        }
    }

    /// <summary>
    /// Reads until the input ends. The loop may have ended first (a failure) and disposed the queue; whatever comes after
    /// that is dropped, so this thread never takes the helper down.
    /// </summary>
    private static void Read(Stream input, BlockingCollection<object> queue)
    {
        object last;
        try
        {
            while (VrProtocol.Read(input) is { } message)
            {
                if (!Post(queue, message))
                {
                    return;
                }
            }

            last = new EndOfInput();
        }
        catch (VrProtocolException e)
        {
            last = e;
        }
        catch (IOException)
        {
            // A broken pipe: the app is gone.
            last = new EndOfInput();
        }
        catch (ObjectDisposedException)
        {
            // The input was closed under the reader.
            last = new EndOfInput();
        }

        Post(queue, last);
    }

    private static bool Post(BlockingCollection<object> queue, object item)
    {
        try
        {
            queue.Add(item);
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static int Fail(TextWriter output, VrFailure failure, string? detail = null)
    {
        Say(output, VrReply.Error(failure.Code));
        if (detail is not null)
        {
            Console.Error.WriteLine(detail);
        }

        return failure.ExitCode;
    }

    private static void Say(TextWriter output, string line)
    {
        try
        {
            output.WriteLine(line);
            output.Flush();
        }
        catch (IOException)
        {
            // Nobody listens any more; the exit code still tells.
        }
    }

    private sealed record EndOfInput;
}
