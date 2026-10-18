using System.Globalization;
using System.Text.RegularExpressions;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Core.Media;

/// <summary>A photo or video shown by the viewer.</summary>
public sealed record LibraryItem(string Path, Guid AlbumId, DateTime Month, DateTime Date, bool IsVideo, long Size);

/// <summary>
/// Lists the media of an album folder. Files under <c>{yyyy}\{yyyy-MM}</c> belong to that month; anything else (copied by
/// hand) to the month of its file date.
/// </summary>
public static partial class PhotoLibrary
{
    public static List<LibraryItem> Scan(Album album)
    {
        if (!Directory.Exists(album.DestinationRoot))
        {
            return [];
        }
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        var work = Path.Combine(album.DestinationRoot, DestinationLayout.WorkFolderName);
        return new DirectoryInfo(album.DestinationRoot)
            .EnumerateFiles("*", options)
            .Where(f => MediaTypes.IsMedia(f.Name) && !f.FullName.StartsWith(work, StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var date = f.LastWriteTime;
                var month = MonthOfFolder(f.DirectoryName) ?? new DateTime(date.Year, date.Month, 1);
                return new LibraryItem(f.FullName, album.Id, month, date, MediaTypes.IsVideo(f.Name), f.Length);
            })
            .OrderByDescending(i => i.Month)
            .ThenByDescending(i => i.Date)
            .ToList();
    }

    private static DateTime? MonthOfFolder(string? directory)
    {
        var match = directory is null ? Match.Empty : MonthFolder().Match(directory);
        return match.Success && DateTime.TryParseExact(match.Groups["m"].Value, "yyyy-MM", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var month) ? month : null;
    }

    [GeneratedRegex(@"[\\/](?<y>\d{4})[\\/](?<m>\k<y>-\d{2})$")]
    private static partial Regex MonthFolder();
}
