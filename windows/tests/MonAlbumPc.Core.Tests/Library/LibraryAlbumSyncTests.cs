using MonAlbumPc.Core.Library;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tests.Library;

public sealed class LibraryAlbumSyncTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AlbumStore _albums;
    private readonly string _root;

    public LibraryAlbumSyncTests()
    {
        _albums = new AlbumStore(new Database(_temp.Combine("catalog.db")));
        _root = _temp.Combine("library");
    }

    public void Dispose() => _temp.Dispose();

    private bool Sync(string? root = null, params LibraryAlbum[] albums) =>
        LibraryAlbumSync.Apply(_albums, new LibraryManifest(1, "NAS", albums), root ?? _root, TimeProvider.System);

    [Fact]
    public void Published_albums_are_added_under_the_library_root()
    {
        var id = Guid.NewGuid();

        Assert.True(Sync(albums: new LibraryAlbum(id, "Marie", "#8764B8", "Famille/Marie")));

        var album = Assert.Single(_albums.List());
        Assert.Equal(id, album.Id);
        Assert.Equal("Marie", album.Name);
        Assert.Equal("#8764B8", album.Color);
        Assert.Equal(Path.Combine(_root, "Famille", "Marie"), album.DestinationRoot);
    }

    [Fact]
    public void Nothing_to_do_changes_nothing()
    {
        var album = new LibraryAlbum(Guid.NewGuid(), "Marie", "#8764B8", "Marie");
        Sync(albums: album);
        var changes = 0;
        _albums.Changed += () => changes++;

        Assert.False(Sync(albums: album));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Albums_are_matched_by_id_so_a_rename_keeps_the_album()
    {
        var id = Guid.NewGuid();
        Sync(albums: new LibraryAlbum(id, "Marie", "#8764B8", "Marie"));
        var created = _albums.Get(id)!.CreatedAt;

        Assert.True(Sync(albums: new LibraryAlbum(id, "Marie D.", "#CA5010", "Dupont/Marie")));

        var album = Assert.Single(_albums.List());
        Assert.Equal(("Marie D.", "#CA5010", created), (album.Name, album.Color, album.CreatedAt));
        Assert.Equal(Path.Combine(_root, "Dupont", "Marie"), album.DestinationRoot);
    }

    [Fact]
    public void Albums_no_longer_published_are_removed()
    {
        var kept = new LibraryAlbum(Guid.NewGuid(), "Marie", "#8764B8", "Marie");
        Sync(albums: [kept, new LibraryAlbum(Guid.NewGuid(), "Paul", "#8764B8", "Paul")]);

        Assert.True(Sync(albums: kept));

        Assert.Equal("Marie", Assert.Single(_albums.List()).Name);
    }

    [Fact]
    public void Moving_the_library_follows_with_the_same_albums()
    {
        var album = new LibraryAlbum(Guid.NewGuid(), "Marie", "#8764B8", "Marie");
        Sync(albums: album);
        var moved = _temp.Combine("ailleurs");

        Assert.True(Sync(moved, album));

        Assert.Equal(Path.Combine(moved, "Marie"), Assert.Single(_albums.List()).DestinationRoot);
    }

    [Fact]
    public void Albums_that_would_lead_outside_the_library_are_ignored()
    {
        Sync(albums: [new LibraryAlbum(Guid.NewGuid(), "Sortie", "#8764B8", "../Ailleurs"), new LibraryAlbum(Guid.Empty, "Sans id", "#8764B8", "A"),
            new LibraryAlbum(Guid.NewGuid(), " ", "#8764B8", "B")]);

        Assert.Empty(_albums.List());
    }

    [Fact]
    public void An_invalid_color_cannot_reach_the_interface()
    {
        Sync(albums: new LibraryAlbum(Guid.NewGuid(), "Marie", "rouge", "Marie"));

        Assert.Equal(Album.Palette[0], Assert.Single(_albums.List()).Color);
    }
}
