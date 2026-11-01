using System.Text.RegularExpressions;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Library;

/// <summary>Makes the local albums of a viewer match the published ones, by id.</summary>
public static partial class LibraryAlbumSync
{
    /// <summary>Adds, updates and removes local albums. Returns true when something changed.</summary>
    public static bool Apply(AlbumStore albums, LibraryManifest manifest, string libraryRoot, TimeProvider clock)
    {
        var published = manifest.Albums
            .Where(a => a.Id != Guid.Empty && !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => (Album: a, Root: LibraryManifest.AlbumRoot(libraryRoot, a.Folder ?? "")))
            .Where(a => a.Root is not null)
            .DistinctBy(a => a.Album.Id)
            .ToList();
        var local = albums.List().ToDictionary(a => a.Id);
        var changed = false;

        foreach (var album in local.Values.Where(a => published.All(p => p.Album.Id != a.Id)))
        {
            albums.Delete(album.Id);
            changed = true;
        }
        foreach (var (album, root) in published)
        {
            // The color ends up in a brush: a value from a network folder must not be able to break the page.
            var color = album.Color is not null && ColorFormat().IsMatch(album.Color) ? album.Color : Album.Palette[0];
            if (!local.TryGetValue(album.Id, out var existing))
            {
                albums.Add(new Album(album.Id, album.Name, root!, color, clock.GetLocalNow()));
                changed = true;
            }
            else if (existing.Name != album.Name || existing.DestinationRoot != root || existing.Color != color)
            {
                albums.Update(existing with { Name = album.Name, DestinationRoot = root!, Color = color });
                changed = true;
            }
        }
        return changed;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorFormat();
}
