using System.Globalization;
using PitVoice.Core.Audio;
using PitVoice.Core.Languages;
using PitVoice.Speech.Audio;

namespace PitVoice.Speech.Samples;

/// <summary>A key the user answers with during scripted recording.</summary>
public enum ScriptKey
{
    /// <summary>Enter: record, or save the take just recorded.</summary>
    Enter,

    /// <summary>R: record the phrase again.</summary>
    Retake,

    /// <summary>S: skip the phrase.</summary>
    Skip,

    /// <summary>Q: quit.</summary>
    Quit,
}

/// <summary>The console of scripted recording; replaced by a fake in tests.</summary>
public interface IScriptConsole
{
    ScriptKey ReadKey();

    void WriteLine(string text);
}

/// <summary>What a session did.</summary>
/// <param name="Saved">Takes saved, in order.</param>
/// <param name="Skipped">Phrase numbers skipped.</param>
/// <param name="Quit">True when the user quit before the end of the list.</param>
public sealed record ScriptedRecordingResult(IReadOnlyList<string> Saved, IReadOnlyList<int> Skipped, bool Quit);

/// <summary>
/// <c>listen --script &lt;file&gt; --save-wav &lt;directory&gt;</c> (design D9): shows the phrases of a script one by one,
/// records each when the user presses Enter, prints what was recognized and parsed, and on a second Enter saves
/// <c>take-NNN.wav</c> (NNN — the number of the phrase in the script) and appends its entry to <c>samples.yaml</c> in the
/// directory. R records again, S skips, Q quits. Nothing is written without confirmation, and an existing take is never
/// overwritten, so a session can continue with <c>--from</c>. Audio is written only here and only to the directory given
/// on the command line (the single exception of TZ §11).
/// </summary>
public sealed class ScriptedRecording
{
    private readonly IScriptConsole _console;
    private readonly Func<AudioBuffer?> _record;
    private readonly Func<AudioBuffer, string> _describe;

    /// <param name="console">Keys and output.</param>
    /// <param name="record">Records one take (start, wait for the stop key, stop); null when recording failed.</param>
    /// <param name="describe">What the take was recognized and parsed as, for the user to judge it.</param>
    public ScriptedRecording(IScriptConsole console, Func<AudioBuffer?> record, Func<AudioBuffer, string> describe)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(describe);
        _console = console;
        _record = record;
        _describe = describe;
    }

    /// <summary>
    /// Why the command cannot start, checked before the microphone opens: no save directory, no script, a bad number.
    /// Null when everything is in place.
    /// </summary>
    public static string? CheckArguments(string? scriptPath, string? saveDirectory, int from, int scriptLength)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory))
        {
            return "--script needs --save-wav <directory>: the directory where takes and samples.yaml are saved";
        }

        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            return "--script needs a file";
        }

        if (from < 1 || from > scriptLength)
        {
            return $"--from must be between 1 and {scriptLength.ToString(CultureInfo.InvariantCulture)}";
        }

        return null;
    }

    /// <summary>The file a take of phrase <paramref name="number"/> is saved to.</summary>
    public static string TakeFile(int number) => $"take-{number.ToString("000", CultureInfo.InvariantCulture)}.wav";

    public ScriptedRecordingResult Run(IReadOnlyList<SampleEntry> script, string directory, int from = 1)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var samples = Path.Combine(directory, SampleFile.FileName);
        var saved = new List<string>();
        var skipped = new List<int>();

        for (var number = from; number <= script.Count; number++)
        {
            var phrase = script[number - 1];
            _console.WriteLine(string.Empty);
            _console.WriteLine($"[{number}/{script.Count}] say: \"{phrase.Say}\"   expect: {SampleFile.FormatExpect(phrase.Expect)}");
            var file = TakeFile(number);
            if (File.Exists(Path.Combine(directory, file)))
            {
                _console.WriteLine($"{file} already exists: skipped, nothing is overwritten");
                skipped.Add(number);
                continue;
            }

            _console.WriteLine("Enter: record, S: skip, Q: quit");
            switch (_console.ReadKey())
            {
                case ScriptKey.Quit:
                    return new ScriptedRecordingResult(saved, skipped, Quit: true);
                case ScriptKey.Skip:
                    skipped.Add(number);
                    continue;
            }

            while (true)
            {
                var take = _record();
                if (take is null || take.IsEmpty)
                {
                    _console.WriteLine("no audio. Enter or R: record again, S: skip, Q: quit");
                }
                else
                {
                    _console.WriteLine(_describe(take));
                    _console.WriteLine("Enter: save, R: record again, S: skip, Q: quit");
                }

                var key = _console.ReadKey();
                if (key == ScriptKey.Quit)
                {
                    return new ScriptedRecordingResult(saved, skipped, Quit: true);
                }

                if (key == ScriptKey.Skip)
                {
                    skipped.Add(number);
                    break;
                }

                if (key == ScriptKey.Retake || take is null || take.IsEmpty)
                {
                    continue;
                }

                WavFile.Save(take, Path.Combine(directory, file));
                SampleFile.Append(samples, phrase with { File = file, Line = null });
                saved.Add(file);
                _console.WriteLine($"saved: {file}");
                break;
            }
        }

        return new ScriptedRecordingResult(saved, skipped, Quit: false);
    }
}
