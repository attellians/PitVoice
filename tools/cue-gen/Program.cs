using System.Buffers.Binary;
using System.Globalization;

// Generates the three built-in cues in the "soft sine" preset picked by ear (refresh-cue-sounds): one timbre for all
// three cues, told apart by melody and register.
//
//   - voice: a plain sine, linear 12 ms attack, then an exponential decay with a time constant of a third of the note;
//   - sub-bass: a sine an octave or two below (the tone halved until it is at most 160 Hz, not below 45 Hz) at 0.175
//     against 0.3 for the voice, starting at 1.25x its pitch and gliding down to it within 50 ms, under the same envelope;
//   - a second-order low-pass at 2500 Hz, Q 0.7, over the sum;
//   - room: the dry sound plus 0.12 of it convolved with 1.4 s of decaying noise ((1 - t/T)^3, unit energy);
//   - start is one note, success goes up a fourth, error goes down a fourth in a lower register.
//
// The free tails are longer than the limits (start 150 ms, others 400 ms), so each cue is cut with a cosine fade-out.
// All three are scaled by one factor so the loudest peak is 0.45 of full scale. 16-bit PCM, 44.1 kHz, mono. The noise
// comes from xorshift32 with a fixed seed, so running the tool again gives byte-identical files.
//
//   dotnet run --project tools/cue-gen [-- <output directory>]
//
// Default output: src/PitVoice.Speech/Cues in the repository.

const int sampleRate = 44100;
const double baseHz = 520;
var fourth = Math.Pow(2, 5.0 / 12);

var cues = new (string Name, Cut Cut, Note[] Notes)[]
{
    // One short note: "speak now".
    ("start", new Cut(150, 20), [new Note(0, baseHz, 110)]),

    // Up a fourth: "done".
    ("success", new Cut(400, 40), [new Note(0, baseHz, 132), new Note(120, baseHz * fourth, 220)]),

    // Down a fourth, lower: "something went wrong".
    ("error", new Cut(400, 40), [new Note(0, baseHz * 0.6, 154), new Note(160, baseHz * 0.6 / fourth, 220)]),
};

var impulse = RoomImpulse(seconds: 1.4, wet: 0.12);
var rendered = cues.Select(cue => Render(cue.Notes, cue.Cut, impulse)).ToList();
var scale = 0.45 / rendered.Max(samples => samples.Max(Math.Abs));

var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(FindRepositoryRoot(), "src", "PitVoice.Speech", "Cues");
Directory.CreateDirectory(output);
for (var c = 0; c < cues.Length; c++)
{
    var samples = rendered[c].Select(value => (short)Math.Round(value * scale * short.MaxValue)).ToArray();
    var path = Path.Combine(output, cues[c].Name + ".wav");
    File.WriteAllBytes(path, Wav(samples));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{path}: {samples.Length * 1000.0 / sampleRate:0} ms"));
}

return 0;

static double[] Render(Note[] notes, Cut cut, double[] impulse)
{
    var count = cut.Milliseconds * sampleRate / 1000;
    var dry = new double[count];
    foreach (var note in notes)
    {
        AddNote(dry, note);
    }

    LowPass(dry, cutoffHz: 2500, q: 0.7);

    // Only the first `count` samples are kept, so the convolution needs no more of the impulse than that.
    var mixed = new double[count];
    for (var i = 0; i < count; i++)
    {
        var wet = 0.0;
        var taps = Math.Min(i + 1, impulse.Length);
        for (var k = 0; k < taps; k++)
        {
            wet += dry[i - k] * impulse[k];
        }

        mixed[i] = dry[i] + wet;
    }

    var fade = cut.FadeMilliseconds * sampleRate / 1000;
    for (var k = 0; k < fade; k++)
    {
        mixed[count - fade + k] *= 0.5 * (1 + Math.Cos(Math.PI * (k + 1) / fade));
    }

    return mixed;
}

static void AddNote(double[] buffer, Note note)
{
    const double voiceLevel = 0.3;
    const double subLevel = 0.175;
    const double attackSeconds = 0.012;
    const double pushSeconds = 0.05;
    const double push = 1.25;

    var subHz = note.Hz;
    while (subHz > 160 && subHz / 2 >= 45)
    {
        subHz /= 2;
    }

    var decay = note.Milliseconds / 1000.0 / 3;
    var start = note.AtMilliseconds * sampleRate / 1000;
    double voicePhase = 0, subPhase = 0;
    for (var i = start; i < buffer.Length; i++)
    {
        var t = (double)(i - start) / sampleRate;
        var envelope = t < attackSeconds ? t / attackSeconds : Math.Exp(-(t - attackSeconds) / decay);

        // Exponential glide from the push down to the sub-bass pitch; phases are accumulated so nothing clicks.
        var subNow = t < pushSeconds ? subHz * push * Math.Pow(1 / push, t / pushSeconds) : subHz;
        buffer[i] += envelope * ((voiceLevel * Math.Sin(voicePhase)) + (subLevel * Math.Sin(subPhase)));
        voicePhase += 2 * Math.PI * note.Hz / sampleRate;
        subPhase += 2 * Math.PI * subNow / sampleRate;
    }
}

// RBJ cookbook low-pass, direct form I.
static void LowPass(double[] buffer, double cutoffHz, double q)
{
    var w = 2 * Math.PI * cutoffHz / sampleRate;
    var alpha = Math.Sin(w) / (2 * q);
    var a0 = 1 + alpha;
    var b0 = (1 - Math.Cos(w)) / 2 / a0;
    var b1 = (1 - Math.Cos(w)) / a0;
    var b2 = b0;
    var a1 = -2 * Math.Cos(w) / a0;
    var a2 = (1 - alpha) / a0;

    double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
    for (var i = 0; i < buffer.Length; i++)
    {
        var x = buffer[i];
        var y = (b0 * x) + (b1 * x1) + (b2 * x2) - (a1 * y1) - (a2 * y2);
        (x2, x1, y2, y1) = (x1, x, y1, y);
        buffer[i] = y;
    }
}

// Decaying noise with unit energy, times the wet level. xorshift32 instead of System.Random: its sequence is not
// promised to stay the same between .NET versions, and the files must be reproducible byte for byte.
static double[] RoomImpulse(double seconds, double wet)
{
    var length = (int)(seconds * sampleRate);
    var impulse = new double[length];
    var state = 0x2545F491u;
    var energy = 0.0;
    for (var i = 0; i < length; i++)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        var noise = (state / (double)uint.MaxValue * 2) - 1;
        var tail = 1 - ((double)i / length);
        impulse[i] = noise * tail * tail * tail;
        energy += impulse[i] * impulse[i];
    }

    var gain = wet / Math.Sqrt(energy);
    for (var i = 0; i < length; i++)
    {
        impulse[i] *= gain;
    }

    return impulse;
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

/// <summary>One note: when it starts, its pitch, and its length (sets the decay; the tail rings on past it).</summary>
internal sealed record Note(int AtMilliseconds, double Hz, int Milliseconds);

/// <summary>Where the cue is cut and how long the cosine fade-out before the cut is.</summary>
internal sealed record Cut(int Milliseconds, int FadeMilliseconds);
