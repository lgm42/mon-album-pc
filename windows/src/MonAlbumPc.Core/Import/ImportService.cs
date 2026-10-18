using System.Diagnostics;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Import;

/// <summary>A fully received file waiting to be placed in its album.</summary>
public sealed record IncomingFile(
    string TempPath,
    string Sha256,
    string OriginalName,
    Guid AlbumId,
    Guid? DeviceId,
    ImportChannel Channel,
    string? SourcePath = null,
    DateTime? PhoneTakenAt = null,
    DateTimeOffset? ModifiedAt = null);

public enum ImportOutcome { Saved, AlreadyPresent }

public sealed record ImportResult(ImportOutcome Outcome, string? DestinationPath);

/// <summary>Places received files in their album folder, without duplicates, and records them.</summary>
public sealed class ImportService(AlbumStore albums, ImportHistory history, TimeProvider clock)
{
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);

    private readonly Lock _placementLock = new();

    /// <summary>
    /// Imports <paramref name="file"/>. The temporary file is always consumed (moved or deleted), except when
    /// the destination is unavailable, so that the upload can be kept for later.
    /// </summary>
    public ImportResult Import(IncomingFile file)
    {
        var album = albums.Get(file.AlbumId) ?? throw new InvalidOperationException($"Album {file.AlbumId} inconnu.");
        if (!Directory.Exists(album.DestinationRoot))
        {
            throw new DestinationUnavailableException(album.DestinationRoot);
        }

        var actual = Hashing.Sha256File(file.TempPath);
        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file.TempPath);
            throw new HashMismatchException(file.Sha256, actual);
        }

        var originalName = SafeFileName(file.OriginalName);
        var fileDate = file.ModifiedAt?.LocalDateTime ?? File.GetLastWriteTime(file.TempPath);
        var taken = TakenDateResolver.Resolve(file.TempPath, originalName, file.PhoneTakenAt, fileDate);
        var convert = MediaTypes.NeedsJpegConversion(originalName);
        var finalName = convert ? Path.ChangeExtension(originalName, ".jpg") : originalName;

        if (IsAlreadyPresent(album.Id, actual, originalName, finalName, taken))
        {
            File.Delete(file.TempPath);
            return new ImportResult(ImportOutcome.AlreadyPresent, null);
        }

        var directory = DestinationLayout.TargetDirectory(album.DestinationRoot, taken.Value);
        string produced;
        try
        {
            Directory.CreateDirectory(directory);
            produced = file.TempPath;
            if (convert)
            {
                produced = Path.Combine(directory, $".{Guid.NewGuid():N}.converting");
                JpegConverter.Convert(file.TempPath, produced);
                File.Delete(file.TempPath);
            }
        }
        catch (IOException e) when (e.HResult is ErrorDiskFull or ErrorHandleDiskFull)
        {
            throw new DiskFullException(album.DestinationRoot, e);
        }

        var destination = Place(produced, directory, finalName);
        if (destination is null)
        {
            return new ImportResult(ImportOutcome.AlreadyPresent, null);
        }
        if (file.ModifiedAt is { } modified)
        {
            File.SetLastWriteTimeUtc(destination, modified.UtcDateTime);
        }

        history.Record(new ImportRecord(
            actual, album.Id, file.DeviceId, new FileInfo(destination).Length, file.OriginalName, file.SourcePath,
            destination, taken.Value, file.Channel, clock.GetLocalNow()));
        return new ImportResult(ImportOutcome.Saved, destination);
    }

    private bool IsAlreadyPresent(Guid albumId, string sha256, string originalName, string finalName, TakenDate taken)
    {
        if (history.Contains(albumId, sha256))
        {
            return true;
        }
        // Loose match only when the date is trustworthy, otherwise unrelated "IMG_0001.jpg" files would collide.
        return taken.Source is TakenDateSource.Exif or TakenDateSource.FileName
               && (history.ContainsByNameAndDate(albumId, originalName, taken.Value)
                   || history.ContainsByNameAndDate(albumId, finalName, taken.Value));
    }

    /// <summary>Moves <paramref name="produced"/> to the first free name. Returns null if an identical file is already there.</summary>
    private string? Place(string produced, string directory, string fileName)
    {
        lock (_placementLock)
        {
            string? producedHash = null;
            foreach (var candidate in DestinationLayout.Candidates(directory, fileName))
            {
                if (!File.Exists(candidate))
                {
                    File.Move(produced, candidate);
                    return candidate;
                }
                if (new FileInfo(candidate).Length == new FileInfo(produced).Length)
                {
                    producedHash ??= Hashing.Sha256File(produced);
                    if (Hashing.Sha256File(candidate) == producedHash)
                    {
                        File.Delete(produced);
                        return null;
                    }
                }
            }
        }
        throw new UnreachableException();
    }

    private static string SafeFileName(string name)
    {
        var fileName = Path.GetFileName(name);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }
        return string.IsNullOrWhiteSpace(fileName) ? "photo" : fileName;
    }
}
