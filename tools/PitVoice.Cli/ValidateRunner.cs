using PitVoice.Core.Languages;

namespace PitVoice.Cli;

internal static class ValidateRunner
{
    public static int Run(string directory)
    {
        Console.WriteLine($"Validating language packs in {directory}");
        var results = LanguagePackLoader.LoadDirectory(directory);
        var errors = 0;
        var warnings = 0;
        var failures = 0;
        var passed = 0;

        foreach (var result in results)
        {
            foreach (var issue in result.Issues)
            {
                Console.WriteLine(issue);
                if (issue.Severity == IssueSeverity.Error)
                {
                    errors++;
                }
                else
                {
                    warnings++;
                }
            }

            if (result.Pack is not { } pack)
            {
                Console.WriteLine($"{result.File}: FAILED validation");
                continue;
            }

            var packFailures = LanguagePackTestRunner.Run(pack);
            foreach (var failure in packFailures)
            {
                Console.WriteLine(failure);
            }

            failures += packFailures.Count;
            passed += pack.Tests.Count - packFailures.Count;
            Console.WriteLine($"{result.File}: {pack.Language} ({pack.Name}), {pack.Intents.Count} intents, {pack.Templates.Count} templates, {pack.Tests.Count - packFailures.Count}/{pack.Tests.Count} tests passed");
        }

        Console.WriteLine($"{results.Count} file(s): {errors} error(s), {warnings} warning(s), {passed} test(s) passed, {failures} test(s) failed");
        return errors > 0 || failures > 0 ? 1 : 0;
    }
}
