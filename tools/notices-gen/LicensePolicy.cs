namespace PitVoice.NoticesGen;

/// <summary>
/// Which licenses may ship (third-party-notices spec: Forbidden licenses in the distribution). Licenses are SPDX
/// expressions; an expression passes when every <c>AND</c> part and at least one <c>OR</c> part is allowed. GPL, LGPL,
/// AGPL and non-commercial licenses are not on the list. The one exception, the compiler runtime under the GCC Runtime
/// Library Exception, passes only for components described by hand, never for a package by its license name alone.
/// </summary>
internal static class LicensePolicy
{
    public static readonly IReadOnlySet<string> Allowed = new SortedSet<string>(StringComparer.Ordinal)
    {
        "0BSD",
        "Apache-2.0",
        "BSD-2-Clause",
        "BSD-3-Clause",
        "CC-BY-4.0",
        "ISC",
        "MIT",
        "Unlicense",
        "Zlib",
    };

    /// <summary>Allowed only for entries of <c>components.json</c>.</summary>
    public static readonly IReadOnlySet<string> AllowedForManualEntries = new SortedSet<string>(StringComparer.Ordinal)
    {
        "GPL-3.0-or-later WITH GCC-exception-3.1",
    };

    /// <summary>True when <paramref name="expression"/> may ship; <paramref name="manual"/> is true for hand-described components.</summary>
    public static bool IsAllowed(string expression, bool manual) => Parse(expression).IsAllowed(manual);

    /// <summary>License and exception ids PitVoice ships the component under (the first allowed choice of an <c>OR</c>), for the license texts.</summary>
    public static IEnumerable<string> Ids(string expression, bool manual) => Parse(expression).Ids(manual);

    /// <exception cref="FormatException">The expression is not valid.</exception>
    public static Node Parse(string expression)
    {
        var tokens = Tokenize(expression);
        var position = 0;
        var node = ParseOr(tokens, ref position, expression);
        if (position != tokens.Count)
        {
            throw new FormatException($"unexpected '{tokens[position]}' in license expression '{expression}'");
        }

        return node;
    }

    private static List<string> Tokenize(string expression)
    {
        var tokens = new List<string>();
        foreach (var part in expression.Replace("(", " ( ", StringComparison.Ordinal).Replace(")", " ) ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            tokens.Add(part);
        }

        if (tokens.Count == 0)
        {
            throw new FormatException("empty license expression");
        }

        return tokens;
    }

    private static Node ParseOr(List<string> tokens, ref int position, string expression)
    {
        var parts = new List<Node> { ParseAnd(tokens, ref position, expression) };
        while (position < tokens.Count && tokens[position] == "OR")
        {
            position++;
            parts.Add(ParseAnd(tokens, ref position, expression));
        }

        return parts.Count == 1 ? parts[0] : new AnyOf(parts);
    }

    private static Node ParseAnd(List<string> tokens, ref int position, string expression)
    {
        var parts = new List<Node> { ParseAtom(tokens, ref position, expression) };
        while (position < tokens.Count && tokens[position] == "AND")
        {
            position++;
            parts.Add(ParseAtom(tokens, ref position, expression));
        }

        return parts.Count == 1 ? parts[0] : new AllOf(parts);
    }

    private static Node ParseAtom(List<string> tokens, ref int position, string expression)
    {
        if (position >= tokens.Count)
        {
            throw new FormatException($"license expression '{expression}' ends too early");
        }

        var token = tokens[position++];
        if (token == "(")
        {
            var inner = ParseOr(tokens, ref position, expression);
            if (position >= tokens.Count || tokens[position] != ")")
            {
                throw new FormatException($"missing ')' in license expression '{expression}'");
            }

            position++;
            return inner;
        }

        if (token is ")" or "AND" or "OR" or "WITH")
        {
            throw new FormatException($"unexpected '{token}' in license expression '{expression}'");
        }

        if (position < tokens.Count && tokens[position] == "WITH")
        {
            position++;
            if (position >= tokens.Count)
            {
                throw new FormatException($"license expression '{expression}' has no exception after WITH");
            }

            return new License(token, tokens[position++]);
        }

        return new License(token, null);
    }

    /// <summary>A parsed expression.</summary>
    internal abstract record Node
    {
        public abstract bool IsAllowed(bool manual);

        public abstract IEnumerable<string> Ids(bool manual);
    }

    private sealed record License(string Id, string? Exception) : Node
    {
        public override bool IsAllowed(bool manual) => Exception is null
            ? Allowed.Contains(Id)
            : manual && AllowedForManualEntries.Contains($"{Id} WITH {Exception}");

        public override IEnumerable<string> Ids(bool manual) => Exception is null ? [Id] : [Id, Exception];
    }

    private sealed record AllOf(IReadOnlyList<Node> Parts) : Node
    {
        public override bool IsAllowed(bool manual) => Parts.All(p => p.IsAllowed(manual));

        public override IEnumerable<string> Ids(bool manual) => Parts.SelectMany(p => p.Ids(manual));
    }

    private sealed record AnyOf(IReadOnlyList<Node> Parts) : Node
    {
        public override bool IsAllowed(bool manual) => Parts.Any(p => p.IsAllowed(manual));

        public override IEnumerable<string> Ids(bool manual) => (Parts.FirstOrDefault(p => p.IsAllowed(manual)) ?? Parts[0]).Ids(manual);
    }
}
