using System.Text.Json;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Core.Import;

/// <summary>What the phone announced about a file before sending it.</summary>
public sealed record UploadDeclaration(
    string Sha256,
    long Size,
    string Name,
    string? SourceFolder,
    string? Mime,
    DateTimeOffset? TakenAt,
    DateTimeOffset? ModifiedAt);

public sealed record Upload(string Id, Guid DeviceId, Guid AlbumId, UploadDeclaration Declaration, DateTimeOffset CreatedAt);

/// <summary>
/// Partial uploads, kept next to their album (<c>{root}\.monalbumpc\uploads</c>) so that the final move stays on
/// the same disk. A connection cut mid-file resumes from the bytes already written.
/// </summary>
public sealed class UploadStore(TimeProvider clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly Lock _lock = new();

    /// <summary>Returns the pending upload of this file by this phone, or starts a new one.</summary>
    public Upload Begin(Album album, Guid deviceId, UploadDeclaration declaration)
    {
        var directory = UploadsDirectory(album);
        lock (_lock)
        {
            var existing = List(directory).FirstOrDefault(u =>
                u.DeviceId == deviceId && string.Equals(u.Declaration.Sha256, declaration.Sha256, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }
            var upload = new Upload(Guid.NewGuid().ToString("N"), deviceId, album.Id, declaration, clock.GetUtcNow());
            File.WriteAllText(MetadataPath(directory, upload.Id), JsonSerializer.Serialize(upload));
            File.Create(DataPath(directory, upload.Id)).Dispose();
            return upload;
        }
    }

    public Upload? Find(Album album, string uploadId)
    {
        if (!IsValidId(uploadId))
        {
            return null;
        }
        var path = MetadataPath(UploadsDirectory(album), uploadId);
        return File.Exists(path) ? JsonSerializer.Deserialize<Upload>(File.ReadAllText(path)) : null;
    }

    public long Offset(Album album, Upload upload) => new FileInfo(DataPath(UploadsDirectory(album), upload.Id)).Length;

    public string DataPath(Album album, Upload upload) => DataPath(UploadsDirectory(album), upload.Id);

    /// <summary>
    /// Appends <paramref name="body"/> at <paramref name="offset"/>. Bytes are flushed as they come, so an
    /// interrupted request still advances the offset. Returns the new offset.
    /// </summary>
    public async Task<long> AppendAsync(Album album, Upload upload, long offset, Stream body, CancellationToken cancellationToken)
    {
        var path = DataPath(UploadsDirectory(album), upload.Id);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true);
        if (file.Length != offset)
        {
            throw new OffsetMismatchException(file.Length);
        }
        file.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[1 << 16];
        try
        {
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                var room = upload.Declaration.Size - file.Position;
                if (read > room)
                {
                    throw new InvalidDataException("Le téléphone envoie plus d'octets que la taille annoncée.");
                }
                await file.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None);
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException)
        {
            // Connection lost: keep what was received, the phone resumes from there.
        }
        await file.FlushAsync(CancellationToken.None);
        return file.Length;
    }

    public void Delete(Album album, Upload upload)
    {
        var directory = UploadsDirectory(album);
        File.Delete(MetadataPath(directory, upload.Id));
        File.Delete(DataPath(directory, upload.Id));
    }

    /// <summary>Removes uploads abandoned for more than <see cref="Retention"/>.</summary>
    public void PurgeExpired(Album album)
    {
        var directory = Path.Combine(album.DestinationRoot, DestinationLayout.WorkFolderName, "uploads");
        if (!Directory.Exists(directory))
        {
            return;
        }
        lock (_lock)
        {
            foreach (var upload in List(directory).Where(u => clock.GetUtcNow() - u.CreatedAt > Retention))
            {
                File.Delete(MetadataPath(directory, upload.Id));
                File.Delete(DataPath(directory, upload.Id));
            }
        }
    }

    private static string UploadsDirectory(Album album)
    {
        if (!Directory.Exists(album.DestinationRoot))
        {
            throw new DestinationUnavailableException(album.DestinationRoot);
        }
        var work = Path.Combine(album.DestinationRoot, DestinationLayout.WorkFolderName);
        var uploads = Path.Combine(work, "uploads");
        if (!Directory.Exists(uploads))
        {
            Directory.CreateDirectory(uploads);
            File.SetAttributes(work, File.GetAttributes(work) | FileAttributes.Hidden);
        }
        return uploads;
    }

    private static IEnumerable<Upload> List(string directory) =>
        Directory.EnumerateFiles(directory, "*.json")
            .Select(path => JsonSerializer.Deserialize<Upload>(File.ReadAllText(path)))
            .OfType<Upload>();

    private static bool IsValidId(string id) => id.Length == 32 && id.All(char.IsAsciiHexDigit);

    private static string MetadataPath(string directory, string id) => Path.Combine(directory, id + ".json");

    private static string DataPath(string directory, string id) => Path.Combine(directory, id + ".part");
}

/// <summary>The phone resumed from a different offset than what the PC holds.</summary>
public sealed class OffsetMismatchException(long expected)
    : Exception($"Reprise à l'octet {expected} attendue.")
{
    public long Expected { get; } = expected;
}
