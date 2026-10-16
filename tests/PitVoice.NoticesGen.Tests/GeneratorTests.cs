using Xunit;

namespace PitVoice.NoticesGen.Tests;

/// <summary>The notices generator on prepared files (design decision 8, third-party-notices spec).</summary>
public sealed class GeneratorTests : IDisposable
{
    private readonly NoticesFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Notices_list_a_package_with_version_license_and_the_license_text()
    {
        _fixture.Package("Vosk", "0.3.38", "Apache-2.0");

        var output = _fixture.Generate();

        Assert.Contains("| Vosk | 0.3.38 | Apache-2.0 | Copyright Vosk authors | https://example.org/Vosk |", output.Markdown, StringComparison.Ordinal);
        Assert.Contains("### Apache-2.0", output.Markdown, StringComparison.Ordinal);
        Assert.Contains("Apache License", output.Markdown, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Vosk\"", output.Json, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"0.3.38\"", output.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void Notices_list_models_with_license_and_source()
    {
        _fixture.Package("Vosk", "0.3.38", "Apache-2.0");

        var output = _fixture.Generate();

        Assert.Contains("| vosk-model-small-ru-0.22 | Apache-2.0 | https://alphacephei.com/vosk/models |", output.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("vosk-model", output.Json, StringComparison.Ordinal); // "About" lists models from the manifest
    }

    [Fact]
    public void Package_license_cannot_be_determined()
    {
        _fixture.Package("Mystery", "1.0.0", license: null);

        var error = Assert.Throws<NoticesException>(_fixture.Generate);

        var problem = Assert.Single(error.Problems);
        Assert.StartsWith("Mystery 1.0.0: license cannot be determined", problem, StringComparison.Ordinal);
        Assert.Contains("components.json", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Compiler_runtime_with_the_exception_passes_when_described_by_hand()
    {
        _fixture.Package("Vosk", "0.3.38", "Apache-2.0")
            .Component("libstdc++ (GCC runtime)", "from Vosk 0.3.38", "GPL-3.0-or-later WITH GCC-exception-3.1", "C++ runtime of libvosk.dll");

        var output = _fixture.Generate();

        Assert.Contains("| libstdc++ (GCC runtime) | from Vosk 0.3.38 | GPL-3.0-or-later WITH GCC-exception-3.1 |", output.Markdown, StringComparison.Ordinal);
        Assert.Contains("### GCC-exception-3.1", output.Markdown, StringComparison.Ordinal);
        Assert.Contains("GCC RUNTIME LIBRARY EXCEPTION", output.Markdown, StringComparison.Ordinal);
        Assert.Contains("### GPL-3.0-or-later", output.Markdown, StringComparison.Ordinal);
        Assert.Contains("- **libstdc++ (GCC runtime)**: C++ runtime of libvosk.dll", output.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_exception_is_not_allowed_for_a_package_by_its_license_name()
    {
        _fixture.Package("GccRuntime", "8.3.0", "GPL-3.0-or-later WITH GCC-exception-3.1");

        var error = Assert.Throws<NoticesException>(_fixture.Generate);

        Assert.StartsWith("GccRuntime 8.3.0: license GPL-3.0-or-later WITH GCC-exception-3.1 may not ship", Assert.Single(error.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void GPL_dependency_added_names_the_package_license_and_the_chain()
    {
        _fixture.Package("Copyleft", "2.0.0", "GPL-3.0-only", direct: false)
            .Package("Wrapper", "1.0.0", "MIT", direct: true, "Copyleft");

        var error = Assert.Throws<NoticesException>(_fixture.Generate);

        var problem = Assert.Single(error.Problems);
        Assert.StartsWith("Copyleft 2.0.0: license GPL-3.0-only may not ship", problem, StringComparison.Ordinal);
        Assert.EndsWith("comes through PitVoice → Wrapper → Copyleft", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LGPL-2.1-only")]
    [InlineData("AGPL-3.0-or-later")]
    [InlineData("CC-BY-NC-4.0")]
    [InlineData("MIT AND GPL-2.0-only")]
    public void Other_forbidden_licenses_fail(string license)
    {
        _fixture.Package("Forbidden", "1.0.0", license);

        Assert.Throws<NoticesException>(_fixture.Generate);
    }

    [Fact]
    public void Only_allowed_licenses_pass_and_the_files_are_sorted()
    {
        _fixture.Package("Zeta", "1.0.0", "Zlib")
            .Package("alpha", "2.0.0", "BSD-3-Clause")
            .Package("Mid", "1.0.0", "MIT OR GPL-2.0-only")
            .Package("Beta", "3.0.0", "Apache-2.0");

        var output = _fixture.Generate();

        Assert.Equal(["alpha", "Beta", "Mid", "Zeta"], output.Components.Select(c => c.Name));
        foreach (var text in new[] { "### Apache-2.0", "### BSD-3-Clause", "### MIT", "### Zlib" })
        {
            Assert.Contains(text, output.Markdown, StringComparison.Ordinal);
        }

        Assert.Equal(output.Markdown, _fixture.Generate().Markdown); // deterministic
    }

    [Fact]
    public void Package_added_without_regenerating_is_named_in_the_differences()
    {
        _fixture.Package("Vosk", "0.3.38", "Apache-2.0");
        var committed = _fixture.Generate().Json;
        _fixture.Package("NewThing", "1.2.3", "MIT");

        var differences = Generator.Differences(_fixture.Generate().Components, committed);

        Assert.Equal(["not listed yet: NewThing 1.2.3 (MIT)"], differences);
    }

    [Fact]
    public void License_without_its_text_fails()
    {
        _fixture.Package("Isc", "1.0.0", "ISC");

        var error = Assert.Throws<NoticesException>(_fixture.Generate);

        Assert.Equal("no text for license ISC: add tools/notices-gen/licenses/ISC.txt", Assert.Single(error.Problems));
    }

    [Theory]
    [InlineData("MIT", false, true)]
    [InlineData("MIT OR Apache-2.0", false, true)]
    [InlineData("(MIT OR GPL-3.0-only) AND Apache-2.0", false, true)]
    [InlineData("MIT AND GPL-3.0-only", false, false)]
    [InlineData("GPL-3.0-or-later WITH GCC-exception-3.1", true, true)]
    [InlineData("GPL-3.0-or-later WITH GCC-exception-3.1", false, false)]
    [InlineData("GPL-3.0-or-later", true, false)]
    [InlineData("Apache-2.0 WITH LLVM-exception", true, false)]
    public void Policy_reads_spdx_expressions(string expression, bool manual, bool allowed) =>
        Assert.Equal(allowed, LicensePolicy.IsAllowed(expression, manual));

    [Theory]
    [InlineData("")]
    [InlineData("MIT OR")]
    [InlineData("(MIT")]
    [InlineData("MIT WITH")]
    public void Broken_expressions_are_rejected(string expression) =>
        Assert.Throws<FormatException>(() => LicensePolicy.Parse(expression));
}
