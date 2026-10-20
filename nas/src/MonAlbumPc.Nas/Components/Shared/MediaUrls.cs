namespace MonAlbumPc.Nas.Components.Shared;

/// <summary>URLs of the media endpoints (see <c>MediaEndpoints</c>).</summary>
public static class MediaUrls
{
    public static string Thumbnail(string path, int size, long version = 0, bool fit = false) =>
        $"media/thumb?path={Uri.EscapeDataString(path)}&size={size}" + (fit ? "&fit=true" : "")
        + (version == 0 ? "" : $"&v={version}");

    public static string File(string path, long version = 0) =>
        $"media/file?path={Uri.EscapeDataString(path)}" + (version == 0 ? "" : $"&v={version}");
}
