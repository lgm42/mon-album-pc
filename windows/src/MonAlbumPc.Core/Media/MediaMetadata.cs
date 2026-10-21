using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;

namespace MonAlbumPc.Core.Media;

/// <summary>A place on Earth, in decimal degrees (WGS 84).</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    /// <summary>
    /// A point, or null when the values cannot be a real shooting place: out of range, or exactly (0, 0), which phones
    /// write when they had no fix.
    /// </summary>
    public static GeoPoint? Create(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsNaN(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
        {
            return null;
        }
        if (Math.Abs(latitude) < 1e-6 && Math.Abs(longitude) < 1e-6)
        {
            return null;
        }
        return new GeoPoint(latitude, longitude);
    }
}

/// <summary>What the catalog keeps about a media file: when and where it was taken, when the file says so.</summary>
public readonly record struct MediaInfo(DateTime? TakenAt, GeoPoint? Location);

/// <summary>
/// Reads the shooting date and place of a photo (EXIF GPS) or a video (QuickTime <c>ISO 6709</c> location, either in the
/// <c>com.apple.quicktime.location.ISO6709</c> metadata key or in the <c>©xyz</c> user-data atom written by Android).
/// </summary>
public static partial class MediaMetadata
{
    private static readonly byte[] XyzAtom = [0xA9, (byte)'x', (byte)'y', (byte)'z'];

    public static MediaInfo Read(string path)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(path);
        }
        catch (Exception e) when (e is ImageProcessingException or IOException or UnauthorizedAccessException)
        {
            directories = [];
        }
        var location = ReadLocation(directories);
        if (location is null && MediaTypes.IsVideo(path))
        {
            location = ReadUserDataLocation(path);
        }
        return new MediaInfo(TakenDateResolver.ReadMetadataDate(directories), location);
    }

    private static GeoPoint? ReadLocation(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        foreach (var gps in directories.OfType<GpsDirectory>())
        {
            try
            {
                if (gps.TryGetGeoLocation(out var geo) && GeoPoint.Create(geo.Latitude, geo.Longitude) is { } point)
                {
                    return point;
                }
            }
            catch (Exception e) when (e is MetadataException or FormatException or InvalidCastException)
            {
                // Damaged GPS block: no place rather than no photo.
            }
        }
        foreach (var header in directories.OfType<QuickTimeMetadataHeaderDirectory>())
        {
            if (header.GetString(QuickTimeMetadataHeaderDirectory.TagGpsLocation) is { } iso && ParseIso6709(iso) is { } point)
            {
                return point;
            }
        }
        return null;
    }

    /// <summary>
    /// Parses an ISO 6709 position such as <c>+48.8584+002.2945/</c> or <c>+48.8584+002.2945+035.000/</c>; degrees and
    /// minutes (<c>+4851.50+00217.67/</c>) are accepted too.
    /// </summary>
    public static GeoPoint? ParseIso6709(string text)
    {
        var match = Iso6709().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }
        var latitude = ToDegrees(match.Groups["lat"].Value, degreeDigits: 2);
        var longitude = ToDegrees(match.Groups["lon"].Value, degreeDigits: 3);
        return latitude is { } lat && longitude is { } lon ? GeoPoint.Create(lat, lon) : null;
    }

    /// <summary>±DD.DDD, ±DDMM.MMM or ±DDMMSS.SSS (three digits of degrees for longitudes).</summary>
    private static double? ToDegrees(string value, int degreeDigits)
    {
        var sign = value[0] == '-' ? -1 : 1;
        var digits = value[1..];
        var point = digits.IndexOf('.');
        var integerDigits = point < 0 ? digits.Length : point;
        if (!double.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }
        var result = (integerDigits - degreeDigits) switch
        {
            0 => number,
            2 => Math.Floor(number / 100) + number % 100 / 60,
            4 => Math.Floor(number / 10000) + Math.Floor(number % 10000 / 100) / 60 + number % 100 / 3600,
            _ => (double?)null,
        };
        return result * sign;
    }

    /// <summary>The <c>moov/udta/©xyz</c> atom: a 16-bit length, a 16-bit language, then the ISO 6709 text.</summary>
    private static GeoPoint? ReadUserDataLocation(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            if (FindAtom(stream, 0, stream.Length, "moov"u8) is not { } moov
                || FindAtom(stream, moov.Start, moov.End, "udta"u8) is not { } udta
                || FindAtom(stream, udta.Start, udta.End, XyzAtom) is not { } xyz
                || xyz.End - xyz.Start is < 5 or > 256)
            {
                return null;
            }
            var data = new byte[xyz.End - xyz.Start];
            stream.Position = xyz.Start;
            stream.ReadExactly(data);
            var length = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(data), data.Length - 4);
            return ParseIso6709(Encoding.UTF8.GetString(data, 4, length));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Finds a child atom between <paramref name="start"/> and <paramref name="end"/>; returns its payload range.</summary>
    private static (long Start, long End)? FindAtom(Stream stream, long start, long end, ReadOnlySpan<byte> type)
    {
        Span<byte> header = stackalloc byte[16];
        var position = start;
        while (position + 8 <= end)
        {
            stream.Position = position;
            stream.ReadExactly(header[..8]);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var headerSize = 8;
            if (size == 1)
            {
                stream.ReadExactly(header.Slice(8, 8));
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = end - position; // up to the end of the file
            }
            if (size < headerSize || position + size > end)
            {
                return null;
            }
            if (header.Slice(4, 4).SequenceEqual(type))
            {
                return (position + headerSize, position + size);
            }
            position += size;
        }
        return null;
    }

    [GeneratedRegex(@"^(?<lat>[+-]\d+(\.\d+)?)(?<lon>[+-]\d+(\.\d+)?)")]
    private static partial Regex Iso6709();
}
