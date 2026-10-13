using Xunit;

namespace PitVoice.Models.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void Manifest_ShippedFile_HasAsrModelForBothLanguages()
    {
        var manifest = ModelManifest.Load(TestPaths.Manifest);

        foreach (var language in new[] { "ru", "en" })
        {
            var entry = Assert.Single(manifest.Entries, e => e.Type == ModelType.Asr && e.Language == language);
            Assert.False(string.IsNullOrWhiteSpace(entry.Id));
            Assert.Equal(Uri.UriSchemeHttps, entry.Url.Scheme);
            Assert.True(entry.SizeBytes > 0);
            Assert.Equal(64, entry.Sha256.Length);
            Assert.False(string.IsNullOrWhiteSpace(entry.License));
            Assert.False(string.IsNullOrWhiteSpace(entry.Source));
        }
    }

    [Theory]
    [InlineData("ru", "vosk-model-small-ru-0.22")]
    [InlineData("en", "vosk-model-small-en-us-0.15")]
    public void Manifest_ShippedFile_ContainsModelReferencedByPack(string language, string id)
    {
        var entry = ModelManifest.Load(TestPaths.Manifest).Find(id);

        Assert.NotNull(entry);
        Assert.Equal(language, entry.Language);
    }

    [Fact]
    public void Manifest_Malformed_FailsWithFileName()
    {
        var path = TestPaths.Fixture("malformed.json");

        var error = Assert.Throws<ModelManifestException>(() => ModelManifest.Load(path));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_EntryWithoutSha256_FailsWithFileAndEntry()
    {
        var path = TestPaths.Fixture("missing-sha256.json");

        var error = Assert.Throws<ModelManifestException>(() => ModelManifest.Load(path));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
        Assert.Contains("test-no-sha", error.Message, StringComparison.Ordinal);
        Assert.Contains("sha256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_MissingFile_FailsWithFileName()
    {
        var path = TestPaths.Fixture("no-such-manifest.json");

        var error = Assert.Throws<ModelManifestException>(() => ModelManifest.Load(path));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_DuplicateId_Fails()
    {
        const string entry = """
            { "id": "dup", "type": "asr", "language": "ru", "url": "https://example.org/a.zip", "sizeBytes": 1,
              "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "license": "MIT", "source": "x" }
            """;

        var error = Assert.Throws<ModelManifestException>(() => ModelManifest.Parse($"{{ \"models\": [{entry}, {entry}] }}", "dup.json"));

        Assert.Contains("duplicate", error.Message, StringComparison.Ordinal);
    }
}
