using MonAlbumPc.Core;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Nas.Components.Shared;
using MonAlbumPc.Nas.Media;

namespace MonAlbumPc.Nas.Components.Photos;

/// <summary>What the viewer tells about a photo or video (as the information pane of the Windows viewer).</summary>
public sealed record PhotoDetails(DateTime Taken, string Phone, string Dimensions, string Folder)
{
    /// <summary>Reads the file metadata and the import history: call it off the render thread.</summary>
    public static PhotoDetails Read(CoreServices core, PhotoEntry entry)
    {
        var taken = Safe(() => TakenDateResolver.ReadMetadataDate(entry.Path)) ?? entry.Item.Date;
        var phone = Safe(() => core.History.DeviceOf(entry.Path) is { } id ? core.Devices.Get(id)?.Name : null)
                    ?? Safe(() => core.Journals.DeviceOf(entry.Album, entry.Path))
                    ?? "Inconnu (copiée à la main)";
        var size = Formatting.Size(entry.Item.Size);
        var dimensions = entry.IsVideo ? null : MediaImages.Dimensions(entry.Path);
        var folder = Path.GetDirectoryName(entry.Relative.Replace('/', Path.DirectorySeparatorChar)) is { Length: > 0 } dir
            ? $"{entry.Album.Name} · {dir.Replace(Path.DirectorySeparatorChar, '/')}/"
            : entry.Album.Name;
        return new PhotoDetails(taken, phone, dimensions is { } d ? $"{d.Width} × {d.Height} · {size}" : size, folder);
    }

    private static T? Safe<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            return default;
        }
    }
}
