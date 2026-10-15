using System.Buffers.Binary;
using System.Globalization;

// Generates the three built-in cues (design decision 2): sine tones with a short attack and a smooth release, plus a
// little of the 2nd and 3rd harmonics so they cut through engine noise. 16-bit PCM, 44.1 kHz, mono. The output depends
// only on the numbers below, so running the tool again gives byte-identical files.
//
//   dotnet run --project tools/cue-gen [-- <output directory>]
//
// Default output: src/PitVoice.Speech/Cues in the repository.

const int sampleRate = 44100;

var cues = new Dictionary<string, Tone[]>
{
    // Short rising tone: "speak now". At most 150 ms.
    ["start"] = [new Tone(StartHz: 660, EndHz: 990, Milliseconds: 110, Harmonic3: 0)],

    // Two rising tones: "done".
    ["success"] =
    [
        new Tone(660, 660, 100, Harmonic3: 0),
        Tone.Silence(25),
        new Tone(990, 990, 160, Harmonic3: 0),
    ],

    // Two falling, lower and buzzier tones: "something went wrong".
    ["error"] =
    [
        new Tone(392, 392, 140, Harmonic3: 0.3),
        Tone.Silence(40),
        new Tone(262, 262, 200, Harmonic3: 0.3),
    ],
};

var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(FindRepositoryRoot(), "src", "PitVoice.Speech", "Cues");
Directory.CreateDirectory(output);
foreach (var (name, tones) in cues)
{
    var samples = tones.SelectMany(Render).ToArray();
    var path = Path.Combine(output, name + ".wav");
    File.WriteAllBytes(path, Wav(samples));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{path}: {samples.Length * 1000.0 / sampleRate:0} ms"));
}

return 0;

static IEnumerable<short> Render(Tone tone)
{
    const double amplitude = 0.45;
    const double attackMs = 4;
    const double releaseMs = 45;

    var count = tone.Milliseconds * sampleRate / 1000;
    if (tone.StartHz == 0)
    {
        for (var i = 0; i < count; i++)
        {
            yield return 0;
        }

        yield break;
    }

    var attack = attackMs * sampleRate / 1000;
    var release = releaseMs * sampleRate / 1000;
    var peak = 1 + 0.15 + tone.Harmonic3;
    var phase = 0.0;
    for (var i = 0; i < count; i++)
    {
        // Exponential glide from StartHz to EndHz; the phase is accumulated so the glide has no clicks.
        var t = (double)i / count;
        var frequency = tone.StartHz * Math.Pow((double)tone.EndHz / tone.StartHz, t);
        phase += 2 * Math.PI * frequency / sampleRate;

        var envelope = 1.0;
        if (i < attack)
        {
            envelope = i / attack;
        }
        else if (i > count - release)
        {
            envelope = 0.5 * (1 + Math.Cos(Math.PI * (i - (count - release)) / release));
        }

        var value = Math.Sin(phase) + (0.15 * Math.Sin(2 * phase)) + (tone.Harmonic3 * Math.Sin(3 * phase));
        yield return (short)Math.Round(value / peak * envelope * amplitude * short.MaxValue);
    }
}

static byte[] Wav(short[] samples)
{
    const int headerSize = 44;
    var dataSize = samples.Length * 2;
    var bytes = new byte[headerSize + dataSize];
    var span = bytes.AsSpan();
    "RIFF"u8.CopyTo(span);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataSize);
    "WAVE"u8.CopyTo(span[8..]);
    "fmt "u8.CopyTo(span[12..]);
    BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
    BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1); // PCM
    BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1); // mono
    BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
    BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * 2);
    BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
    BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
    "data"u8.CopyTo(span[36..]);
    BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);
    for (var i = 0; i < samples.Length; i++)
    {
        BinaryPrimitives.WriteInt16LittleEndian(span[(headerSize + (i * 2))..], samples[i]);
    }

    return bytes;
}

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "PitVoice.sln")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("run from inside the repository or pass the output directory");
}

/// <summary>One tone, or silence when the frequency is 0.</summary>
internal sealed record Tone(int StartHz, int EndHz, int Milliseconds, double Harmonic3)
{
    public static Tone Silence(int milliseconds) => new(0, 0, milliseconds, 0);
}
