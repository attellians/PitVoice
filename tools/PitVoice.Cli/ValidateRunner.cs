using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Models;
using PitVoice.Speech.Asr;

namespace PitVoice.Cli;

internal static class ValidateRunner
{
    public static async Task<int> RunAsync(string directory, ModelStore store, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Validating language packs in {directory}");
        var results = LanguagePackLoader.LoadDirectory(directory);
        var additionsDirectory = UserAdditionsLoader.ResolveDirectory(null);
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

            var additions = UserAdditions.Apply(pack, additionsDirectory);
            if (additions.Exists)
            {
                foreach (var issue in additions.Issues)
                {
                    Console.WriteLine(issue);
                }

                errors += additions.Errors.Count();
                warnings += additions.Warnings.Count();
                Console.WriteLine(additions.Applied
                    ? $"{additions.File}: {additions.Pack.Templates.Count(t => t.IsUserAddition)} own pattern(s) added to {pack.Language}"
                    : $"{additions.File}: FAILED validation, the built-in {pack.Language} pack is used");
            }

            warnings += await CheckModelVocabularyAsync(additions.Pack, result.File, store, cancellationToken).ConfigureAwait(false);
        }

        foreach (var orphan in UserAdditions.FindOrphans(additionsDirectory, results.Where(r => r.IsSuccess).Select(r => r.Pack!.Language)))
        {
            Console.WriteLine(orphan);
            warnings++;
        }

        Console.WriteLine($"{results.Count} file(s): {errors} error(s), {warnings} warning(s), {passed} test(s) passed, {failures} test(s) failed");
        return errors > 0 || failures > 0 ? 1 : 0;
    }

    /// <summary>
    /// Compares the pack vocabulary with its installed ASR model. Words the model does not know can never be recognized;
    /// they are warnings, not errors, because the pack itself is still valid for typed input.
    /// </summary>
    private static async Task<int> CheckModelVocabularyAsync(LanguagePack pack, string file, ModelStore store, CancellationToken cancellationToken)
    {
        if (pack.AsrModel is not { } modelId)
        {
            return 0;
        }

        if (store.GetInstalledPath(modelId) is not { } modelPath)
        {
            Console.WriteLine($"{file}: vocabulary check skipped: model {modelId} is not installed in {store.Root} (pitvoice-cli models download {modelId})");
            return 0;
        }

        var (report, error) = await VoskAsrEngine.CheckVocabularyAsync(pack, modelPath, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            Console.WriteLine(new PackIssue(IssueSeverity.Warning, file, null, "asr_model", $"vocabulary check failed: {error}"));
            return 1;
        }

        foreach (var word in report.Missing)
        {
            var message = $"word '{word}' is not in model {modelId} and cannot be recognized; used in {Usage(pack, word)}";
            Console.WriteLine(new PackIssue(IssueSeverity.Warning, file, null, "asr_model", message));
        }

        if (report.Substituted.Count > 0)
        {
            var pairs = string.Join(", ", report.Substituted.Select(p => $"{p.Key} → {p.Value}"));
            Console.WriteLine($"{file}: grammar uses the model spelling for {report.Substituted.Count} word(s): {pairs}");
        }

        Console.WriteLine($"{file}: vocabulary checked against {modelId}: {report.Missing.Count} word(s) missing");
        return report.Missing.Count;
    }

    private static string Usage(LanguagePack pack, string word)
    {
        var templates = pack.Templates
            .Where(t => t.Tokens.Any(token => token is WordLiteral literal && literal.Text == word))
            .Select(t => $"{t.Intent} \"{t.Source}\"")
            .ToArray();
        if (templates.Length > 0)
        {
            return string.Join("; ", templates);
        }

        if (pack.Units.Values.Any(u => u.Synonyms.Contains(word)))
        {
            return "unit synonyms";
        }

        if (pack.NumberParser.Words.Contains(word))
        {
            return "number words";
        }

        if (pack.Conjunctions.Any(c => c.Contains(word)))
        {
            return "conjunctions";
        }

        return pack.Fillers.Contains(word) ? "fillers" : pack.Negations.Contains(word) ? "negations" : "vocabulary";
    }
}
