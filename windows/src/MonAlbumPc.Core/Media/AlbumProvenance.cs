using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Media;

/// <summary>
/// Provenance of the files of an album, loaded once (catalog and journals) so that thousands of files can be classified
/// without a query each.
/// </summary>
public sealed class AlbumProvenance
{
    private readonly string _root;
    private readonly Dictionary<string, string> _folders;

    private AlbumProvenance(string root, Dictionary<string, string> folders)
    {
        _root = root;
        _folders = folders;
    }

    public static AlbumProvenance Load(ImportHistory history, Album album) => new(album.DestinationRoot, history.SourceFolders(album));

    /// <summary>Phone folder of a file of the album, when an import recorded it.</summary>
    public string? FolderOf(string fullPath) =>
        _folders.GetValueOrDefault(Path.GetRelativePath(_root, fullPath).Replace('\\', '/'));

    public MediaSource Of(string fullPath) => MediaSource.Classify(FolderOf(fullPath), fullPath);
}
