using System.Buffers.Binary;
using ImageMagick;

namespace MonAlbumPc.Core.Media;

/// <summary>
/// EXIF orientation (tag 0x0112) of photos. Rotating a JPEG only rewrites that tag in place, so the image is never
/// re-encoded (no quality loss).
/// </summary>
public static class ExifOrientation
{
    private const ushort OrientationTag = 0x0112;

    // EXIF orientation values for upright, 90° clockwise, 180° and 90° counter-clockwise (no mirroring).
    private static readonly ushort[] Clockwise = [1, 6, 3, 8];

    /// <summary>Clockwise rotation in degrees (0, 90, 180, 270) needed to show the photo upright.</summary>
    public static int Degrees(ushort orientation) => orientation switch
    {
        6 or 7 => 90,
        3 or 4 => 180,
        8 or 5 => 270,
        _ => 0,
    };

    /// <summary>Orientation stored in a JPEG, 1 when absent or unreadable.</summary>
    public static ushort Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return FindTag(bytes) is { } tag ? Value(bytes, tag) : (ushort)1;
    }

    /// <summary>Rotates a photo by a quarter turn, without re-encoding JPEGs.</summary>
    public static void Rotate(string path, bool clockwise)
    {
        var bytes = File.ReadAllBytes(path);
        if (FindTag(bytes) is { } tag)
        {
            var current = Array.IndexOf(Clockwise, Value(bytes, tag));
            var next = Clockwise[((current < 0 ? 0 : current) + (clockwise ? 1 : 3)) % 4];
            var span = bytes.AsSpan(tag.ValueOffset, 2);
            if (tag.LittleEndian) BinaryPrimitives.WriteUInt16LittleEndian(span, next);
            else BinaryPrimitives.WriteUInt16BigEndian(span, next);
            WriteAtomically(path, bytes);
            return;
        }

        // No orientation tag (or not a JPEG): rotate the pixels. Lossless for PNG; JPEGs without EXIF are rare.
        using var image = new MagickImage(bytes);
        image.Rotate(clockwise ? 90 : 270);
        if (image.Format == MagickFormat.Jpeg)
        {
            image.Quality = JpegConverter.Quality;
        }
        WriteAtomically(path, image.ToByteArray());
    }

    private static ushort Value(byte[] bytes, Tag tag) => tag.LittleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(tag.ValueOffset))
        : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tag.ValueOffset));

    /// <summary>Locates the orientation entry of IFD0 in the EXIF (APP1) segment of a JPEG.</summary>
    private static Tag? FindTag(byte[] b)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8)
        {
            return null;
        }
        var pos = 2;
        while (pos + 4 <= b.Length && b[pos] == 0xFF)
        {
            var marker = b[pos + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(pos + 2));
            if (marker == 0xDA || length < 2)
            {
                return null; // image data starts: no EXIF before it
            }
            var segment = pos + 4;
            if (marker == 0xE1 && segment + 14 <= b.Length && b.AsSpan(segment, 6).SequenceEqual("Exif\0\0"u8))
            {
                var tiff = segment + 6;
                var little = b[tiff] == (byte)'I';
                ushort U16(int at) => little ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(at));
                uint U32(int at) => little ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at));
                var ifd = tiff + (int)U32(tiff + 4);
                if (ifd + 2 > b.Length)
                {
                    return null;
                }
                var count = U16(ifd);
                for (var i = 0; i < count; i++)
                {
                    var entry = ifd + 2 + i * 12;
                    if (entry + 12 > b.Length)
                    {
                        return null;
                    }
                    if (U16(entry) == OrientationTag)
                    {
                        return new Tag(entry + 8, little);
                    }
                }
                return null;
            }
            pos = segment + length - 2;
        }
        return null;
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var temp = path + ".rotating";
        var modified = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
        File.SetLastWriteTimeUtc(path, modified);
    }

    private readonly record struct Tag(int ValueOffset, bool LittleEndian);
}
