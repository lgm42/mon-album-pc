using System.Globalization;

namespace MonAlbumPc.Core.Import;

/// <summary>Where a file goes inside an album folder: <c>{root}\{yyyy}\{yyyy-MM}\{name}</c>.</summary>
public static class DestinationLayout
{
    /// <summary>Hidden working folder of the app inside each album root (partial uploads).</summary>
    public const string WorkFolderName = ".monalbumpc";

    public static string TargetDirectory(string root, DateTime takenAt) =>
        Path.Combine(
            root,
            takenAt.ToString("yyyy", CultureInfo.InvariantCulture),
            takenAt.ToString("yyyy-MM", CultureInfo.InvariantCulture));

    /// <summary>
    /// Candidate paths for <paramref name="fileName"/> in <paramref name="directory"/>: the name itself, then
    /// <c>name_1.ext</c>, <c>name_2.ext</c>… The caller takes the first free one, or stops on an identical file.
    /// </summary>
    public static IEnumerable<string> Candidates(string directory, string fileName)
    {
        yield return Path.Combine(directory, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 1; ; i++)
        {
            yield return Path.Combine(directory, $"{stem}_{i}{extension}");
        }
    }
}
