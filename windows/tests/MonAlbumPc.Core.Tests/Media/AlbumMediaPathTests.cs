using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class AlbumMediaPathTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public AlbumMediaPathTests()
    {
        _temp.WriteFile(Path.Combine("Marie", "2026", "2026-11", "IMG_1.jpg"), [1]);
        _temp.WriteFile(Path.Combine("Marie", "2026", "2026-11", "notes.txt"), [1]);
        _temp.WriteFile(Path.Combine("Marie", ".monalbumpc", "corbeille", "2026", "2026-11", "IMG_0.jpg"), [1]);
        _temp.WriteFile(Path.Combine("Marie", ".cache", "x.jpg"), [1]);
        _temp.WriteFile(Path.Combine("Marie2", "secret.jpg"), [1]);
        _temp.WriteFile(Path.Combine("outside.jpg"), [1]);
    }

    private string Root => _temp.Combine("Marie");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Relative_path_uses_slashes_and_resolves_back()
    {
        var photo = _temp.Combine("Marie", "2026", "2026-11", "IMG_1.jpg");

        var relative = AlbumMediaPath.Relative(Root, photo);

        Assert.Equal("2026/2026-11/IMG_1.jpg", relative);
        Assert.Equal(photo, AlbumMediaPath.Resolve(Root, relative));
    }

    [Fact]
    public void Album_root_with_a_trailing_separator_works() =>
        Assert.NotNull(AlbumMediaPath.Resolve(Root + Path.DirectorySeparatorChar, "2026/2026-11/IMG_1.jpg"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026/2026-11/missing.jpg")]
    [InlineData("2026/2026-11/notes.txt")]
    [InlineData("2026/2026-11")]
    [InlineData("../outside.jpg")]
    [InlineData("2026/../../outside.jpg")]
    [InlineData("../Marie2/secret.jpg")]
    [InlineData("./2026/2026-11/IMG_1.jpg")]
    [InlineData("2026//2026-11/IMG_1.jpg")]
    [InlineData("/2026/2026-11/IMG_1.jpg")]
    [InlineData(".monalbumpc/corbeille/2026/2026-11/IMG_0.jpg")]
    [InlineData(".cache/x.jpg")]
    [InlineData("2026/2026-11/IMG_1.jpg\0.jpg")]
    public void Paths_outside_the_visible_album_are_refused(string? relative) =>
        Assert.Null(AlbumMediaPath.Resolve(Root, relative));

    [Fact]
    public void Absolute_paths_are_refused()
    {
        Assert.Null(AlbumMediaPath.Resolve(Root, _temp.Combine("outside.jpg")));
        Assert.Null(AlbumMediaPath.Resolve(Root, _temp.Combine("Marie", "2026", "2026-11", "IMG_1.jpg")));
    }

    [Fact]
    public void Backslashes_are_separators_only_on_windows()
    {
        var resolved = AlbumMediaPath.Resolve(Root, @"..\outside.jpg");
        Assert.Null(resolved);
        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(AlbumMediaPath.Resolve(Root, @"2026\2026-11\IMG_1.jpg"));
            Assert.Null(AlbumMediaPath.Resolve(Root, @"C:\outside.jpg"));
        }
    }

    [Fact]
    public void Links_leading_out_of_the_album_are_refused()
    {
        var inside = _temp.Combine("Marie", "linked-inside");
        var outside = _temp.Combine("Marie", "linked-outside");
        try
        {
            Directory.CreateSymbolicLink(inside, _temp.Combine("Marie", "2026"));
            Directory.CreateSymbolicLink(outside, _temp.Combine("Marie2"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // creating links needs the developer mode on Windows
        }

        Assert.NotNull(AlbumMediaPath.Resolve(Root, "linked-inside/2026-11/IMG_1.jpg"));
        Assert.Null(AlbumMediaPath.Resolve(Root, "linked-outside/secret.jpg"));
    }
}
