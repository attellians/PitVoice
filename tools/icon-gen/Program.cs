using System.IO;
using PitVoice.App.Tray;
using PitVoice.Host;

namespace PitVoice.IconGen;

// Renders the PitVoice sign (design decision 8) into an .ico with PNG frames for every size Windows asks for: the
// taskbar, the window title, Explorer views up to "extra large".
//
//   dotnet run --project tools/icon-gen [-- <output file>]
//
// Default output: src/PitVoice.App/Assets/pitvoice.ico in the repository.
internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(FindRepositoryRoot(), "src", "PitVoice.App", "Assets", "pitvoice.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
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
