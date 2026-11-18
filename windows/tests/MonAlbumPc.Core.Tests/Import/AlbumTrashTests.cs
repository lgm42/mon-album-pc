using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Core.Tests.Import;

public sealed class AlbumTrashTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void File_moves_to_the_album_trash_keeping_its_path()
    {
        var root = _temp.Combine("Nico");
        var path = _temp.WriteFile(@"Nico\2026\2026-10\IMG_1.jpg", [1, 2, 3]);

        var target = AlbumTrash.Move(root, path);

        Assert.False(File.Exists(path));
        Assert.Equal(Path.Combine(AlbumTrash.Folder(root), "2026", "2026-10", "IMG_1.jpg"), target);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(target));
    }

    [Fact]
    public void Same_name_twice_does_not_overwrite_the_trash()
    {
        var root = _temp.Combine("Nico");
        AlbumTrash.Move(root, _temp.WriteFile(@"Nico\2026\IMG_1.jpg", [1]));

        var second = AlbumTrash.Move(root, _temp.WriteFile(@"Nico\2026\IMG_1.jpg", [2]));

        Assert.Equal("IMG_1 (2).jpg", Path.GetFileName(second));
        Assert.Equal([2], File.ReadAllBytes(second));
    }

    [Fact]
    public void Trashed_files_leave_the_library()
    {
        var album = new Album(Guid.NewGuid(), "Nico", _temp.Combine("Nico"), "#000", DateTimeOffset.Now);
        var path = _temp.WriteFile(@"Nico\2026\2026-10\IMG_1.jpg", [1]);

        AlbumTrash.Move(album.DestinationRoot, path);

        Assert.Empty(PhotoLibrary.Scan(album));
    }

    [Fact]
    public void Emptying_erases_the_trash_and_nothing_else()
    {
        var root = _temp.Combine("Nico");
        AlbumTrash.Move(root, _temp.WriteFile(@"Nico\2026\IMG_1.jpg", [1, 2]));
        AlbumTrash.Move(root, _temp.WriteFile(@"Nico\2025\IMG_2.jpg", [3]));
        var kept = _temp.WriteFile(@"Nico\2026\IMG_3.jpg", [4]);

        Assert.Equal((2, 3L), AlbumTrash.Measure(root));
        AlbumTrash.Empty(root);

        Assert.Equal((0, 0L), AlbumTrash.Measure(root));
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void An_album_without_trash_has_nothing_to_empty()
    {
        var root = _temp.Combine("Nico");
        Assert.Equal((0, 0L), AlbumTrash.Measure(root));
        AlbumTrash.Empty(root);
    }

    [Theory]
    [InlineData(@"\\192.168.1.200\donnees\Photos\IMG.jpg", true)]
    [InlineData(@"\\NAS\photos\IMG.jpg", true)]
    public void Unc_paths_are_network_paths(string path, bool expected) =>
        Assert.Equal(expected, AlbumTrash.IsNetworkPath(path));

    [Fact]
    public void Local_folders_are_not_network_paths() => Assert.False(AlbumTrash.IsNetworkPath(_temp.Path));
}
