using Xunit;

namespace PitVoice.Models.Tests;

public sealed class ModelStoreTests
{
    [Fact]
    public void Store_DirectoryWithMarker_IsInstalled()
    {
        using var temp = new TempDirectory();
        var store = new ModelStore(temp.Path);
        Directory.CreateDirectory(store.GetModelDirectory("m1"));
        store.WriteMarker("m1", new string('a', 64), DateTimeOffset.UnixEpoch);

        var installed = Assert.Single(store.ListInstalled());

        Assert.Equal("m1", installed.Id);
        Assert.Equal(store.GetModelDirectory("m1"), installed.Directory);
        Assert.Equal(new string('a', 64), installed.Sha256);
        Assert.Equal(store.GetModelDirectory("m1"), store.GetInstalledPath("m1"));
        Assert.Empty(store.ListIncomplete());
    }

    [Fact]
    public void Store_DirectoryWithoutMarker_IsNotInstalled()
    {
        using var temp = new TempDirectory();
        var store = new ModelStore(temp.Path);
        Directory.CreateDirectory(Path.Combine(store.GetModelDirectory("half"), "am"));

        Assert.False(store.IsInstalled("half"));
        Assert.Null(store.GetInstalledPath("half"));
        Assert.Empty(store.ListInstalled());
        Assert.Equal(["half"], store.ListIncomplete());
    }

    [Fact]
    public void Store_Delete_RemovesDirectory()
    {
        using var temp = new TempDirectory();
        var store = new ModelStore(temp.Path);
        Directory.CreateDirectory(store.GetModelDirectory("m1"));
        store.WriteMarker("m1", new string('a', 64), DateTimeOffset.UnixEpoch);

        Assert.True(store.Delete("m1"));

        Assert.False(store.IsInstalled("m1"));
        Assert.False(Directory.Exists(store.GetModelDirectory("m1")));
        Assert.False(store.Delete("m1"));
    }

    [Fact]
    public void Store_Delete_LockedFile_LeavesModelComplete()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // other systems let open files be deleted
        }

        using var temp = new TempDirectory();
        var store = new ModelStore(temp.Path);
        var directory = store.GetModelDirectory("m1");
        Directory.CreateDirectory(Path.Combine(directory, "am"));
        store.WriteMarker("m1", new string('a', 64), DateTimeOffset.UnixEpoch);

        using (new FileStream(Path.Combine(directory, "am", "final.mdl"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => store.Delete("m1"));
        }

        Assert.True(store.IsInstalled("m1"));
        Assert.True(store.Delete("m1"));
        Assert.False(Directory.Exists(directory));
        Assert.False(Directory.Exists(directory + ModelStore.DeletingSuffix));
    }

    [Fact]
    public void Store_MissingRoot_ListsNothing()
    {
        using var temp = new TempDirectory();
        var store = new ModelStore(Path.Combine(temp.Path, "absent"));

        Assert.Empty(store.ListInstalled());
        Assert.Empty(store.ListIncomplete());
    }

    [Fact]
    public void Store_ExplicitRoot_WinsOverDefault()
    {
        using var temp = new TempDirectory();

        Assert.Equal(Path.GetFullPath(temp.Path), new ModelStore(temp.Path).Root);
        Assert.EndsWith(Path.Combine("PitVoice", "models"), ModelStore.DefaultRoot, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void Store_InvalidId_Throws(string id)
    {
        using var temp = new TempDirectory();

        Assert.Throws<ArgumentException>(() => new ModelStore(temp.Path).GetModelDirectory(id));
    }
}
