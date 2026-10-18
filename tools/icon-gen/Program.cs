using System.IO;
using System.Windows.Media.Imaging;
using PitVoice.App.Tray;
using PitVoice.Host;

namespace PitVoice.IconGen;

// Renders the PitVoice sign (design decision 8) into an .ico with PNG frames for every size Windows asks for: the
// taskbar, the window title, Explorer views up to "extra large".
//
//   dotnet run --project tools/icon-gen [-- <output file>]
//
// Default output: src/PitVoice.App/Assets/pitvoice.ico in the repository. An output ending in .png gets one 256 px
// frame: the logo of the README is .github/assets/logo.png.
internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];
    private const int LogoSize = 256;

    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(FindRepositoryRoot(), "src", "PitVoice.App", "Assets", "pitvoice.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (output.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            // The logo of the README: one large frame.
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(IconArt.Render(HostState.Ready, LogoSize)));
            using var png = File.Create(output);
            encoder.Save(png);
            Console.WriteLine($"{output}: {LogoSize} px");
            return 0;
        }

        using (var file = File.Create(output))
        {
            IconArt.WriteIco(file, HostState.Ready, Sizes);
        }

        Console.WriteLine($"{output}: {Sizes.Length} sizes");
        return 0;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PitVoice.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("run from inside the repository or pass the output file");
    }
}
