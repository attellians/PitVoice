using System.Runtime.InteropServices;
using System.Text;

namespace PitVoice.Speech.Asr;

/// <summary>
/// Direct calls into the native Vosk library (shipped by the Vosk package). The package's own C# wrapper marshals
/// strings in the system ANSI code page, which corrupts Cyrillic grammars and model paths on Windows unless the system
/// code page is UTF-8. Here every string crosses the boundary as UTF-8, which is what Vosk expects on every OS.
/// </summary>
internal static partial class VoskNative
{
    private const string Library = "libvosk";

    [LibraryImport(Library, EntryPoint = "vosk_set_log_level")]
    internal static partial void SetLogLevel(int level);

    [LibraryImport(Library, EntryPoint = "vosk_model_new", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial VoskModelHandle ModelNew(string path);

    [LibraryImport(Library, EntryPoint = "vosk_model_free")]
    internal static partial void ModelFree(IntPtr model);

    [LibraryImport(Library, EntryPoint = "vosk_model_find_word", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ModelFindWord(VoskModelHandle model, string word);

    [LibraryImport(Library, EntryPoint = "vosk_recognizer_new_grm", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial VoskRecognizerHandle RecognizerNewGrammar(VoskModelHandle model, float sampleRate, string grammar);

    [LibraryImport(Library, EntryPoint = "vosk_recognizer_set_words")]
    internal static partial void RecognizerSetWords(VoskRecognizerHandle recognizer, int words);

    /// <summary>
    /// With a value above 0 the final result is a list of alternatives with a score each, without per-word confidence.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "vosk_recognizer_set_max_alternatives")]
    internal static partial void RecognizerSetMaxAlternatives(VoskRecognizerHandle recognizer, int maxAlternatives);

    [LibraryImport(Library, EntryPoint = "vosk_recognizer_accept_waveform_s")]
    internal static partial int RecognizerAcceptWaveform(VoskRecognizerHandle recognizer, short[] data, int length);

    /// <summary>Returns a UTF-8 JSON string owned by the recognizer; valid until the next call on it.</summary>
    [LibraryImport(Library, EntryPoint = "vosk_recognizer_final_result")]
    internal static partial IntPtr RecognizerFinalResult(VoskRecognizerHandle recognizer);

    [LibraryImport(Library, EntryPoint = "vosk_recognizer_free")]
    internal static partial void RecognizerFree(IntPtr recognizer);
}

/// <summary>A loaded Vosk model; invalid when the native library could not load the directory.</summary>
internal sealed class VoskModelHandle : SafeHandle
{
    public VoskModelHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    private static readonly Lock CurrentDirectoryLock = new();

    /// <summary>Loads a model directory. Check <see cref="SafeHandle.IsInvalid"/>: Vosk does not throw.</summary>
    /// <remarks>
    /// On Windows libvosk opens model files with narrow C runtime calls that interpret the path in the ANSI code page,
    /// so a non-ASCII path (a Cyrillic user name in %LocalAppData%) cannot be passed at all. The model directory is then
    /// made the current directory for the duration of the load and "." is passed: Windows resolves the relative names
    /// against its Unicode current directory. The model is read completely inside the call.
    /// </remarks>
    public static VoskModelHandle Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!OperatingSystem.IsWindows() || Ascii.IsValid(path))
        {
            return VoskNative.ModelNew(path);
        }

        lock (CurrentDirectoryLock)
        {
            var previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = path;
            try
            {
                return VoskNative.ModelNew(".");
            }
            finally
            {
                Environment.CurrentDirectory = previous;
            }
        }
    }

    /// <summary>True when the model vocabulary contains <paramref name="word"/> exactly.</summary>
    public bool Contains(string word) => VoskNative.ModelFindWord(this, word) >= 0;

    protected override bool ReleaseHandle()
    {
        VoskNative.ModelFree(handle);
        return true;
    }
}

/// <summary>One recognition session over a model.</summary>
internal sealed class VoskRecognizerHandle : SafeHandle
{
    public VoskRecognizerHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>Feeds the whole buffer and returns the final result JSON.</summary>
    public string Recognize(short[] samples)
    {
        VoskNative.RecognizerAcceptWaveform(this, samples, samples.Length);
        return Marshal.PtrToStringUTF8(VoskNative.RecognizerFinalResult(this)) ?? "{}";
    }

    protected override bool ReleaseHandle()
    {
        VoskNative.RecognizerFree(handle);
        return true;
    }
}
