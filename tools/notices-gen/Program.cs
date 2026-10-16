using System.Text;
using PitVoice.NoticesGen;

// Generates THIRD-PARTY-NOTICES.md and src/PitVoice.App/ThirdParty.json (design decision 8). Needs a restore first.
//
//   dotnet run --project tools/notices-gen              writes both files
//   dotnet run --project tools/notices-gen -- --check   fails when they differ from what would be generated
//
// Exit codes: 0 done, 1 files out of date (--check), 2 generation failed (the files are not touched).

var check = args.Contains("--check");
var root = FindRoot(AppContext.BaseDirectory) ?? FindRoot(Directory.GetCurrentDirectory());
if (root is null)
{
    Console.Error.WriteLine("PitVoice.sln not found above the tool or the current directory");
    return 2;
}

var noticesPath = Path.Combine(root, "THIRD-PARTY-NOTICES.md");
var jsonPath = Path.Combine(root, "src", "PitVoice.App", "ThirdParty.json");
NoticesOutput output;
try
{
    output = Generator.Generate(new NoticesInput(
        [
            Path.Combine(root, "src", "PitVoice.App", "obj", "project.assets.json"),
            Path.Combine(root, "tools", "PitVoice.Cli", "obj", "project.assets.json"),
        ],
        Path.Combine(root, "tools", "notices-gen", "components.json"),
        Path.Combine(root, "models", "manifest.json"),
        Path.Combine(root, "tools", "notices-gen", "licenses")));
}
catch (NoticesException e)
{
    Console.Error.WriteLine("Third-party notices not generated:");
    foreach (var problem in e.Problems)
    {
        Console.Error.WriteLine("  " + problem);
    }

    return 2;
}

if (check)
{
    var currentNotices = ReadOrNull(noticesPath);
    var currentJson = ReadOrNull(jsonPath);
    if (currentNotices == output.Markdown && currentJson == output.Json)
    {
        Console.WriteLine($"Third-party notices are up to date ({output.Components.Count} components).");
        return 0;
    }

    Console.Error.WriteLine("THIRD-PARTY-NOTICES.md or src/PitVoice.App/ThirdParty.json is out of date.");
    foreach (var difference in Generator.Differences(output.Components, currentJson))
    {
        Console.Error.WriteLine("  " + difference);
    }

    Console.Error.WriteLine($"Run '{Generator.Command}' and commit both files.");
    return 1;
}

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
File.WriteAllText(noticesPath, output.Markdown, utf8);
File.WriteAllText(jsonPath, output.Json, utf8);
Console.WriteLine($"Wrote {noticesPath} and {jsonPath} ({output.Components.Count} components).");
return 0;

static string? FindRoot(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "PitVoice.sln")))
        {
            return directory.FullName;
        }
    }

    return null;
}

// Git may check text files out with CRLF on Windows; the content is what counts.
static string? ReadOrNull(string path) => File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
