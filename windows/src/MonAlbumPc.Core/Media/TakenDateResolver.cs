using System.Globalization;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;

namespace MonAlbumPc.Core.Media;

public enum TakenDateSource { Exif, FileName, Phone, FileDate }

/// <summary>Shooting date, as a local wall-clock time, and where it came from.</summary>
public readonly record struct TakenDate(DateTime Value, TakenDateSource Source);

/// <summary>
/// Finds when a photo or video was taken: EXIF (or video metadata), then a date in the file name
/// (<c>IMG-20240512-WA0003.jpg</c>), then the date the phone gave, then the file date.
/// </summary>
public static partial class TakenDateResolver
{
    private static readonly DateTime QuickTimeEpoch = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static TakenDate Resolve(string path, string originalName, DateTime? phoneTakenAt, DateTime fileDate)
    {
        if (ReadMetadataDate(path) is { } metadata)
        {
            return new(metadata, TakenDateSource.Exif);
        }
        if (ParseFileName(originalName) is { } fromName)
        {
            return new(fromName, TakenDateSource.FileName);
        }
        if (phoneTakenAt is { } phone)
        {
            return new(Truncate(phone), TakenDateSource.Phone);
        }
        return new(Truncate(fileDate), TakenDateSource.FileDate);
    }

    public static DateTime? ReadMetadataDate(string path)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(path);
        }
        catch (Exception e) when (e is ImageProcessingException or IOException)
        {
            return null;
        }

        foreach (var exif in directories.OfType<ExifSubIfdDirectory>())
        {
            if (exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var original) && IsPlausible(original))
            {
                return Truncate(original);
            }
        }
        foreach (var movie in directories.OfType<QuickTimeMovieHeaderDirectory>())
        {
            // Video creation dates are stored in UTC.
            if (movie.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var created) && created > QuickTimeEpoch.AddDays(1))
            {
                var local = DateTime.SpecifyKind(created, DateTimeKind.Utc).ToLocalTime();
                if (IsPlausible(local))
                {
                    return Truncate(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
                }
            }
        }
        return null;
    }

    public static DateTime? ParseFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        var full = CompactDateTime().Match(name);
        if (full.Success && TryBuild(full, out var compact))
        {
            return compact;
        }
        var separated = SeparatedDateTime().Match(name);
        if (separated.Success && TryBuild(separated, out var dashed))
        {
            return dashed;
        }
        var dateOnly = CompactDate().Match(name);
        if (dateOnly.Success && TryBuild(dateOnly, out var day))
        {
            return day;
        }
        return null;
    }

    private static bool TryBuild(Match match, out DateTime value)
    {
        var g = match.Groups;
        var text = $"{g["y"].Value}-{g["mo"].Value}-{g["d"].Value} " +
                   $"{Or(g["h"], "00")}:{Or(g["mi"], "00")}:{Or(g["s"], "00")}";
        return DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
               && IsPlausible(value);
    }

    private static string Or(Group group, string fallback) => group.Success ? group.Value : fallback;

    private static bool IsPlausible(DateTime value) => value.Year is >= 1990 and <= 2100;

    private static DateTime Truncate(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Unspecified);

    // IMG_20261017_131705, PXL_20261017_131705123, Screenshot_20261017-131705, VID20261017131705
    [GeneratedRegex(@"(?<!\d)(?<y>(19|20)\d{2})(?<mo>\d{2})(?<d>\d{2})[_\-T]?(?<h>\d{2})(?<mi>\d{2})(?<s>\d{2})")]
    private static partial Regex CompactDateTime();

    // 2026-10-17 13.17.05, Screenshot_2026-10-17-13-17-05
    [GeneratedRegex(@"(?<!\d)(?<y>(19|20)\d{2})-(?<mo>\d{2})-(?<d>\d{2})[ _\-T](?<h>\d{2})[.\-_:](?<mi>\d{2})[.\-_:](?<s>\d{2})")]
    private static partial Regex SeparatedDateTime();

    // IMG-20240512-WA0003 (WhatsApp: date only)
    [GeneratedRegex(@"(?<!\d)(?<y>(19|20)\d{2})(?<mo>\d{2})(?<d>\d{2})(?!\d)")]
    private static partial Regex CompactDate();
}
