using Xunit;

namespace PitVoice.Models.Tests;

/// <summary>
/// Only models whose license allows free redistribution may enter the manifest (TZ §13). The allowlist lives here on
/// purpose: extending it is a deliberate edit of this test.
/// </summary>
public sealed class LicenseTests
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "Apache-2.0",
        "MIT",
        "BSD-2-Clause",
        "BSD-3-Clause",
        "CC-BY-4.0",
        "CC-BY-SA-4.0",
        "CC0-1.0",
    };

    private static readonly string[] Forbidden = ["NC", "GPL"];

    [Fact]
    public void License_ShippedManifest_OnlyAllowedLicenses()
    {
        var violations = Violations(ModelManifest.Load(TestPaths.Manifest));

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("license-nc.json", "test-nc", "CC-BY-NC-4.0")]
    [InlineData("license-agpl.json", "test-agpl", "AGPL-3.0")]
    public void License_ForbiddenLicense_ReportedWithIdAndLicense(string fixture, string id, string license)
    {
        var violation = Assert.Single(Violations(ModelManifest.Load(TestPaths.Fixture(fixture))));

        Assert.Contains(id, violation, StringComparison.Ordinal);
        Assert.Contains(license, violation, StringComparison.Ordinal);
    }

    [Fact]
    public void License_Apache_Passes()
    {
        var entry = new ModelEntry("ok", ModelType.Asr, "en", new Uri("https://example.org/ok.zip"), null, 1, new string('0', 64), "Apache-2.0", "x");

        Assert.Null(Check(entry));
    }

    private static List<string> Violations(ModelManifest manifest) =>
        manifest.Entries.Select(Check).OfType<string>().ToList();

    private static string? Check(ModelEntry entry)
    {
        var license = entry.License;
        var forbidden = Forbidden.Any(f => license.Contains(f, StringComparison.OrdinalIgnoreCase));
        return Allowed.Contains(license) && !forbidden
            ? null
            : $"model '{entry.Id}' has license '{license}' which is not in the allowlist";
    }
}
