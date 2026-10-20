using ImageMagick;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Nas.Components.Shared;

namespace MonAlbumPc.Nas.Components.Photos;

/// <summary>What the information pane shows about a photo or video. Reads the file: build it off the render thread.</summary>
public sealed record PhotoDetails(string Name, string Taken, string TakenShort, string Phone, string DimensionsAndSize, string Album, string Folder)
{
    public static PhotoDetails Read(CoreServices core, LibraryItem item, string? albumName)
    {
        var taken = item.IsVideo ? null : TakenDateResolver.ReadMetadataDate(item.Path);
        var date = taken ?? item.Date;
        var phone = core.History.DeviceOf(item.Path) is { } id ? core.Devices.Get(id)?.Name : null;
        var folder = Path.GetDirectoryName(item.Path) + Path.DirectorySeparatorChar;
        return new PhotoDetails(
            Path.GetFileName(item.Path),
            date.ToString("d MMMM yyyy 'à' HH:mm", Formatting.French),
            date.ToString("d MMMM yyyy", Formatting.French),
            phone ?? "Inconnu (copiée à la main)",
            Dimensions(item) + Formatting.Size(item.Size),
            albumName ?? "",
            folder);
    }

    /// <summary>"4000 × 3000 · " from the image header (cheap), as displayed (EXIF orientation applied).</summary>
    private static string Dimensions(LibraryItem item)
    {
        if (item.IsVideo)
        {
            return "";
        }
        try
        {
            var info = new MagickImageInfo(item.Path);
            var (w, h) = ExifOrientation.Degrees(ExifOrientation.Read(item.Path)) % 180 == 0
                ? (info.Width, info.Height)
                : (info.Height, info.Width);
            return $"{w} × {h} · ";
        }
        catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
