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

    /// <summary>Spec: Mirror for every model — Manifest with mirrors.</summary>
    [Fact]
    public void Manifest_ShippedFile_HasMirrorInTheModelsRelease()
    {
        var manifest = ModelManifest.Load(TestPaths.Manifest);

        Assert.Empty(MirrorProblems(manifest));
        Assert.Equal(
            "https://github.com/attellians/PitVoice/releases/download/models/vosk-model-small-ru-0.22.zip",
            manifest.Find("vosk-model-small-ru-0.22")!.Mirror!.AbsoluteUri);
    }

    /// <summary>Spec: Mirror for every model — Model added without a mirror.</summary>
    [Theory]
    [InlineData("null", "no mirror")]
    [InlineData("\"https://example.org/a.zip\"", "not in the models release")]
    [InlineData("\"https://github.com/attellians/PitVoice/releases/download/models/b.zip\"", "file name differs")]
    [InlineData("\"https://github.com/attellians/PitVoice/releases/download/v0.1.0/a.zip\"", "not in the models release")]
    public void Model_with_a_missing_or_wrong_mirror_is_reported_with_its_id(string mirror, string problem)
    {
        var manifest = ModelManifest.Parse(
            $$"""
            { "models": [ { "id": "new-model", "type": "asr", "language": "ru", "url": "https://example.org/a.zip", "mirror": {{mirror}},
              "sizeBytes": 1, "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "license": "MIT", "source": "x" } ] }
            """,
            "new.json");

        var reported = Assert.Single(MirrorProblems(manifest));

        Assert.StartsWith("new-model: ", reported, StringComparison.Ordinal);
        Assert.Contains(problem, reported, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every model must have a copy in the project's <c>models</c> release under the file name of its primary address
    /// (design decision 7); reachability is checked by hand, not here (no network in tests).
    /// </summary>
    private static IEnumerable<string> MirrorProblems(ModelManifest manifest)
    {
        const string release = "https://github.com/attellians/PitVoice/releases/download/models/";
        foreach (var entry in manifest.Entries)
        {
            if (entry.Mirror is not { } mirror)
            {
                yield return $"{entry.Id}: no mirror";
            }
            else if (!mirror.AbsoluteUri.StartsWith(release, StringComparison.Ordinal))
            {
                yield return $"{entry.Id}: mirror {mirror} is not in the models release {release}";
            }
            else if (Path.GetFileName(mirror.AbsolutePath) != Path.GetFileName(entry.Url.AbsolutePath))
            {
                yield return $"{entry.Id}: file name differs from the primary address {entry.Url}";
            }
        }
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
