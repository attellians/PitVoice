using System.Diagnostics;
using System.Globalization;
using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Core.Responses;
using PitVoice.Input;
using PitVoice.Models;
using PitVoice.Speech.Asr;
using PitVoice.Speech.Audio;
using PitVoice.Speech.Cues;
using PitVoice.Speech.Samples;

namespace PitVoice.Cli;

/// <summary>Settings shared by <c>recognize</c> and <c>listen</c>.</summary>
internal sealed record SpeechSettings(string PacksDirectory, string Language, ModelStore Store, ParseOptions Options);

/// <summary><c>devices</c>, <c>recognize</c> and <c>listen</c>: speech in, parse result out, nothing executed.</summary>
internal static class SpeechRunner
{
    public static int Devices()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Microphone access is only available on Windows.");
            return 1;
        }

        using var capture = new NAudioCapture();
        var microphones = capture.GetInputDevices();
        Console.WriteLine("Microphones:");
        PrintDevices(microphones, "no microphones found");

        using var cues = new NAudioCuePlayer(null, 0);
        Console.WriteLine();
        Console.WriteLine("Output devices (cues):");
        PrintDevices(cues.GetOutputDevices(), "no output devices found");

        Console.WriteLine();
        Console.WriteLine("Game devices (PTT):");
        var gameDevices = SdlJoystickSource.ListDevices(out var error);
        if (error is not null)
        {
            Console.WriteLine($"  {error}");
        }
        else if (gameDevices.Count == 0)
        {
            Console.WriteLine("  none connected");
        }

        foreach (var device in gameDevices)
        {
            Console.WriteLine($"  {device.Name}  [{device.Guid}]");
        }

        return microphones.Count == 0 ? 1 : 0;
    }

    private static void PrintDevices(IReadOnlyList<AudioDevice> devices, string none)
    {
        if (devices.Count == 0)
        {
            Console.WriteLine($"  {none}");
        }

        foreach (var device in devices)
        {
            Console.WriteLine(device.IsDefault ? $"  {device.Name}  (default)" : $"  {device.Name}");
        }
    }

    public static async Task<int> RecognizeAsync(SpeechSettings settings, string wavPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(wavPath))
        {
            Console.Error.WriteLine($"File not found: {wavPath}");
            return 1;
        }

        AudioBuffer buffer;
        try
        {
            buffer = WavFile.Load(wavPath);
        }
        catch (WavLoadException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }

        using var session = await LoadAsync(settings, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return 1;
        }

        return session.RecognizeAndPrint(buffer) ? 0 : 1;
    }

    public static async Task<int> ListenAsync(SpeechSettings settings, string? microphone, string? saveWav, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Microphone access is only available on Windows.");
            return 1;
        }

        using var session = await LoadAsync(settings, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return 1;
        }

        using var capture = new NAudioCapture();
        var take = 0;
        Console.WriteLine("Press Enter to start recording, Enter again to stop (8 s max). Type q and Enter to quit.");
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line is null || line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            var start = capture.Start(microphone);
            if (start.Warning is not null)
            {
                Console.Error.WriteLine($"warning: {start.Warning}");
            }

            if (!start.Started)
            {
                Console.Error.WriteLine($"error: {start.Error}");
                return 1;
            }

            Console.WriteLine($"recording from '{start.DeviceName}'... press Enter to stop");
            WaitForEnter(AudioBuffer.MaxDuration, cancellationToken);
            var recorded = capture.Stop();
            if (recorded.Error is not null)
            {
                Console.Error.WriteLine($"error: {recorded.Error}");
            }

            if (recorded.Buffer.Truncated)
            {
                Console.WriteLine($"recording cut to {AudioBuffer.MaxDuration.TotalSeconds:0} s");
            }

            take++;
            var target = TakePath(saveWav, take);
            if (WavFile.SaveIfRequested(recorded.Buffer, target))
            {
                Console.WriteLine($"saved: {target}");
            }

            if (recorded.Buffer.IsEmpty)
            {
                Console.WriteLine("no audio");
                continue;
            }

            session.RecognizeAndPrint(recorded.Buffer);
        }

        return 0;
    }

    /// <summary>
    /// <c>listen --script</c> (design D9): records the phrases of a script into the acceptance set. Arguments, the script and
    /// its intents are checked before the model loads and the microphone opens.
    /// </summary>
    public static async Task<int> ListenScriptAsync(
        SpeechSettings settings, string? microphone, string scriptPath, string? saveDirectory, int from, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory))
        {
            Console.Error.WriteLine($"error: {ScriptedRecording.CheckArguments(scriptPath, saveDirectory, from, 0)}");
            return 2;
        }

        IReadOnlyList<SampleEntry> script;
        try
        {
            script = SampleFile.Load(scriptPath, requireFile: false);
        }
        catch (SampleFileException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }

        if (ScriptedRecording.CheckArguments(scriptPath, saveDirectory, from, script.Count) is { } problem)
        {
            Console.Error.WriteLine($"error: {problem}");
            return 2;
        }

        var pack = Packs.LoadLanguage(settings.PacksDirectory, settings.Language, Console.Error);
        if (pack is null)
        {
            return 1;
        }

        var unknown = SampleFile.CheckIntents(script, pack, scriptPath);
        if (unknown.Count > 0)
        {
            foreach (var line in unknown)
            {
                Console.Error.WriteLine($"error: {line}");
            }

            return 1;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Microphone access is only available on Windows.");
            return 1;
        }

        using var session = await LoadAsync(settings, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return 1;
        }

        using var capture = new NAudioCapture();
        AudioBuffer? Record()
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var start = capture.Start(microphone);
            if (start.Warning is not null)
            {
                Console.Error.WriteLine($"warning: {start.Warning}");
            }

            if (!start.Started)
            {
                Console.Error.WriteLine($"error: {start.Error}");
                return null;
            }

            Console.WriteLine($"recording from '{start.DeviceName}'... press Enter to stop");
            WaitForEnter(AudioBuffer.MaxDuration, cancellationToken);
            var recorded = capture.Stop();
            if (recorded.Error is not null)
            {
                Console.Error.WriteLine($"error: {recorded.Error}");
            }

            return recorded.Buffer;
        }

        string Describe(AudioBuffer take)
        {
            session.RecognizeAndPrint(take);
            return string.Empty;
        }

        var directory = Path.GetFullPath(saveDirectory);
        Console.WriteLine($"{script.Count} phrase(s) from {scriptPath}; takes go to {directory}");
        var result = new ScriptedRecording(new ConsoleScriptKeys(), Record, Describe).Run(script, directory, from);
        Console.WriteLine($"saved {result.Saved.Count}, skipped {result.Skipped.Count}{(result.Quit ? ", quit" : string.Empty)}");
        return 0;
    }

    /// <summary>Enter, R, S and Q from the console; other keys are ignored.</summary>
    private sealed class ConsoleScriptKeys : IScriptConsole
    {
        public ScriptKey ReadKey()
        {
            while (true)
            {
                if (Console.IsInputRedirected)
                {
                    var line = Console.ReadLine();
                    switch (line?.Trim().ToLowerInvariant())
                    {
                        case null or "q":
                            return ScriptKey.Quit;
                        case "":
                            return ScriptKey.Enter;
                        case "r":
                            return ScriptKey.Retake;
                        case "s":
                            return ScriptKey.Skip;
                    }

                    continue;
                }

                switch (Console.ReadKey(intercept: true).Key)
                {
                    case ConsoleKey.Enter:
                        return ScriptKey.Enter;
                    case ConsoleKey.R:
                        return ScriptKey.Retake;
                    case ConsoleKey.S:
                        return ScriptKey.Skip;
                    case ConsoleKey.Q or ConsoleKey.Escape:
                        return ScriptKey.Quit;
                }
            }
        }

        public void WriteLine(string text)
        {
            if (text.Length > 0)
            {
                Console.WriteLine(text);
            }
            else
            {
                Console.WriteLine();
            }
        }
    }

    private static async Task<Session?> LoadAsync(SpeechSettings settings, CancellationToken cancellationToken)
    {
        var pack = Packs.LoadLanguage(settings.PacksDirectory, settings.Language, Console.Error);
        if (pack is null)
        {
            return null;
        }

        var modelId = pack.AsrModel;
        if (modelId is null)
        {
            Console.Error.WriteLine($"Language pack '{pack.Language}' names no asr_model.");
            return null;
        }

        var engine = new VoskAsrEngine();
        var started = Stopwatch.GetTimestamp();
        var loaded = await engine.LoadAsync(pack, settings.Store.GetInstalledPath(modelId), cancellationToken).ConfigureAwait(false);
        if (!loaded.IsLoaded)
        {
            engine.Dispose();
            Console.Error.WriteLine(loaded.Error);
            if (!settings.Store.IsInstalled(modelId))
            {
                Console.Error.WriteLine($"Download it with: pitvoice-cli models download {modelId}");
            }

            return null;
        }

        Console.WriteLine($"model {modelId} loaded in {Milliseconds(Stopwatch.GetElapsedTime(started))} ms");
        if (engine.Vocabulary is { Missing.Count: > 0 } vocabulary)
        {
            Console.Error.WriteLine($"warning: {vocabulary.Missing.Count} pack word(s) are not in the model and cannot be recognized: {string.Join(", ", vocabulary.Missing)} (see 'validate')");
        }
        return new Session(pack, engine, settings.Options);
    }

    private static string? TakePath(string? saveWav, int take)
    {
        if (string.IsNullOrWhiteSpace(saveWav) || take == 1)
        {
            return saveWav;
        }

        var directory = Path.GetDirectoryName(saveWav) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(saveWav);
        return Path.Combine(directory, $"{name}-{take}{Path.GetExtension(saveWav)}");
    }

    private static void WaitForEnter(TimeSpan limit, CancellationToken cancellationToken)
    {
        if (Console.IsInputRedirected)
        {
            Console.ReadLine();
            return;
        }

        var deadline = Stopwatch.GetTimestamp() + (long)(limit.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Enter)
            {
                return;
            }

            Thread.Sleep(20);
        }
    }

    private static string Milliseconds(TimeSpan time) => time.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture);

    private sealed class Session(LanguagePack pack, VoskAsrEngine engine, ParseOptions options) : IDisposable
    {
        private readonly SpeechRecognizer _recognizer = new(engine);
        private readonly CommandParser _parser = new(pack);
        private readonly ResponseBuilder _responses = new(pack);

        /// <summary>Prints words, timing and the parse block; returns false on a recognition error.</summary>
        public bool RecognizeAndPrint(AudioBuffer buffer)
        {
            var result = _recognizer.Recognize(buffer);
            Console.WriteLine($"audio:    {result.AudioDuration.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)} s{(buffer.Truncated ? " (truncated)" : string.Empty)}");
            Console.WriteLine($"time:     {Milliseconds(result.RecognitionTime)} ms");
            if (!result.IsSuccess)
            {
                Console.Error.WriteLine($"error:    {result.Error}");
                return false;
            }

            var words = result.Phrase.Words.Select(w =>
                w.Confidence is { } c ? $"{w.Text}:{c.ToString("0.00", CultureInfo.InvariantCulture)}" : w.Text);
            Console.WriteLine($"words:    {string.Join(' ', words)}");
            ParseOutput.Print(_parser, _responses, result.Phrase, options);
            return true;
        }

        public void Dispose() => engine.Dispose();
    }
}
