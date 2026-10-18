using System.Globalization;

namespace PitVoice.Core.Languages;

/// <summary>A word as written in the file, with its class already resolved.</summary>
/// <param name="Class">Class the word is listed in.</param>
/// <param name="Text">Raw text; null when the item is not a scalar.</param>
/// <param name="Path">Location inside <c>lexicon</c>, e.g. <c>tires/targets/lf/0</c>.</param>
internal sealed record RawLexiconEntry(WordClass Class, string? Text, string[] Path);

/// <summary>
/// Reads the <c>lexicon</c> section from its deserialized object graph. An unknown group, role or target is reported with
/// its name and the names the format has; the words themselves are checked by <see cref="LexiconWords"/>.
/// </summary>
internal static class LexiconReader
{
    public static IEnumerable<RawLexiconEntry> Read(
        IEnumerable<KeyValuePair<string, Dictionary<string, object?>?>> groups,
        Action<string, string[]> error)
    {
        var result = new List<RawLexiconEntry>();
        foreach (var (groupName, roles) in groups)
        {
            if (!LexiconSchema.TryGetGroup(groupName, out var group))
            {
                error($"unknown word class group '{groupName}'; the format has: {string.Join(", ", LexiconSchema.GroupNames)}", [groupName]);
                continue;
            }

            if (roles is null || roles.Count == 0)
            {
                error($"group '{groupName}' has no word classes; it has: {string.Join(", ", LexiconSchema.RoleNames(group))}", [groupName]);
                continue;
            }

            foreach (var (roleName, value) in roles)
            {
                ReadRole(group, groupName, roleName, value, result, error);
            }
        }

        return result;
    }

    internal static void ReadRole(
        LexiconGroup group, string groupName, string roleName, object? value, List<RawLexiconEntry> result, Action<string, string[]> error)
    {
        if (roleName == LexiconSchema.TargetsKey && LexiconSchema.HasRole(group, WordRole.Target))
        {
            if (value is not IDictionary<object, object> targets)
            {
                error($"'{groupName}.targets' must map targets ({string.Join(", ", LexiconSchema.TargetNames)}) to words", [groupName, roleName]);
                return;
            }

            foreach (var (key, words) in targets)
            {
                var targetName = key?.ToString() ?? string.Empty;
                if (!LexiconSchema.TryGetTarget(targetName, out var target))
                {
                    error($"unknown tire target '{targetName}'; targets: {string.Join(", ", LexiconSchema.TargetNames)}", [groupName, roleName, targetName]);
                    continue;
                }

                ReadWords(new WordClass(group, WordRole.Target, target), words, [groupName, roleName, targetName], result, error);
            }

            return;
        }

        if (roleName == LexiconSchema.CompoundsKey && LexiconSchema.HasRole(group, WordRole.Compound))
        {
            if (value is not IDictionary<object, object> compounds)
            {
                error($"'{groupName}.compounds' must map compounds ({string.Join(", ", LexiconSchema.CompoundNames)}) to words", [groupName, roleName]);
                return;
            }

            foreach (var (key, words) in compounds)
            {
                var compoundName = key?.ToString() ?? string.Empty;
                if (!LexiconSchema.TryGetCompound(compoundName, out var compound))
                {
                    error($"unknown tire compound '{compoundName}'; compounds: {string.Join(", ", LexiconSchema.CompoundNames)}", [groupName, roleName, compoundName]);
                    continue;
                }

                ReadWords(new WordClass(group, WordRole.Compound, Compound: compound), words, [groupName, roleName, compoundName], result, error);
            }

            return;
        }

        if (!LexiconSchema.TryGetRole(group, roleName, out var role))
        {
            error($"unknown word class '{groupName}.{roleName}'; group '{groupName}' has: {string.Join(", ", LexiconSchema.RoleNames(group))}", [groupName, roleName]);
            return;
        }

        ReadWords(new WordClass(group, role), value, [groupName, roleName], result, error);
    }

    private static void ReadWords(WordClass wordClass, object? value, string[] path, List<RawLexiconEntry> result, Action<string, string[]> error)
    {
        if (value is not IList<object> words)
        {
            error($"'{wordClass}' must be a list of words", path);
            return;
        }

        for (var i = 0; i < words.Count; i++)
        {
            string[] itemPath = [.. path, i.ToString(CultureInfo.InvariantCulture)];
            if (words[i] is not (string or null))
            {
                error($"an entry of '{wordClass}' must be a word or words in quotes", itemPath);
                continue;
            }

            result.Add(new RawLexiconEntry(wordClass, words[i] as string, itemPath));
        }
    }
}
