using MonAlbumPc.Core.Library;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Core.Tests.Library;

public sealed class LibraryManifestTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Round_trip_keeps_albums_and_uses_the_published_format()
    {
        var id = Guid.NewGuid();
        var manifest = new LibraryManifest(LibraryManifest.CurrentFormat, "NAS", [new LibraryAlbum(id, "Nicolas", "#0F6CBD", "Nico/Telephone")]);

        LibraryManifest.Write(_temp.Path, manifest);

        var read = LibraryManifest.Read(_temp.Path);
        Assert.NotNull(read);
        Assert.Equal("NAS", read.ServerName);
        Assert.Equal(manifest.Albums, read.Albums);
        var json = File.ReadAllText(LibraryManifest.FilePath(_temp.Path));
        Assert.Contains("\"format\": 1", json);
        Assert.Contains("\"serverName\": \"NAS\"", json);
        Assert.Contains("\"folder\": \"Nico/Telephone\"", json);
        Assert.False(File.Exists(LibraryManifest.FilePath(_temp.Path) + ".tmp"));
    }

    [Fact]
    public void Writing_replaces_the_previous_manifest()
    {
        LibraryManifest.Write(_temp.Path, new LibraryManifest(1, "NAS", [new LibraryAlbum(Guid.NewGuid(), "A", "#0F6CBD", "A")]));
        LibraryManifest.Write(_temp.Path, new LibraryManifest(1, "NAS", []));

        Assert.Empty(LibraryManifest.Read(_temp.Path)!.Albums);
    }

    [Fact]
    public void Missing_library_reads_as_null() => Assert.Null(LibraryManifest.Read(_temp.Combine("absent")));

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("{ \"format\": 1 }")]
    [InlineData("{ \"format\": 99, \"serverName\": \"NAS\", \"albums\": [] }")]
    public void Damaged_or_future_manifest_reads_as_null(string content)
    {
        _temp.WriteFile(@".monalbumpc\library.json", System.Text.Encoding.UTF8.GetBytes(content));

        Assert.Null(LibraryManifest.Read(_temp.Path));
    }

    [Fact]
    public void Only_albums_inside_the_library_are_published_with_relative_folders()
    {
        var inside = new Album(Guid.NewGuid(), "Nicolas", _temp.Combine("Telephones", "Nico"), "#0F6CBD", DateTimeOffset.Now);
        var deep = inside with { Id = Guid.NewGuid(), DestinationRoot = _temp.Combine("Telephones", "Famille", "Marie") };
        var outside = inside with { Id = Guid.NewGuid(), DestinationRoot = _temp.Combine("Ailleurs") };
        var root = inside with { Id = Guid.NewGuid(), DestinationRoot = _temp.Combine("Telephones") };

        var manifest = LibraryManifest.From(_temp.Combine("Telephones"), "NAS", [inside, deep, outside, root]);

        Assert.Equal(["Nico", "Famille/Marie"], manifest.Albums.Select(a => a.Folder));
    }

    [Theory]
    [InlineData("Nico", true)]
    [InlineData("Famille/Marie", true)]
    [InlineData("", false)]
    [InlineData("../Ailleurs", false)]
    [InlineData("Nico/../../Ailleurs", false)]
    [InlineData("/etc", false)]
    [InlineData(@"C:\Windows", false)]
    [InlineData(@"\\serveur\partage", false)]
    [InlineData(".monalbumpc/activity", false)]
    public void Published_folders_cannot_lead_outside_the_library(string folder, bool accepted)
    {
        var path = LibraryManifest.AlbumRoot(_temp.Path, folder);

        Assert.Equal(accepted, path is not null);
        if (accepted)
        {
            Assert.Equal(_temp.Combine(folder.Split('/')), path);
        }
    }
}
