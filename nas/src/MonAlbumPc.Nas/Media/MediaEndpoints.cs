using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Nas.Media;

/// <summary>
/// Photos and videos of the albums for the Photos page, all behind the administrator sign-in:
/// <c>/media/thumb</c> (small JPEG), <c>/media/view</c> (full screen, JPEG when the browser cannot show the original) and
/// <c>/media/original</c> (the file itself, with ranges for videos; <c>download=1</c> to save it). Each takes
/// <c>album</c> (album id) and <c>path</c> (relative to the album folder, see <see cref="AlbumMediaPath"/>); anything
/// else is a 404.
/// </summary>
public static class MediaEndpoints
{
    private const string LongCache = "private, max-age=86400";
    private const string Revalidate = "private, no-cache";

    private static readonly FileExtensionContentTypeProvider ContentTypes = CreateContentTypes();

    // Shown until the background generator has made the thumbnail of a video.
    private static readonly byte[] VideoPlaceholder = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 160 160">
          <rect width="160" height="160" fill="#3a3d46"/>
          <circle cx="80" cy="80" r="28" fill="rgba(255,255,255,.18)"/>
          <path d="M71 66v28l23-14z" fill="#fff"/>
        </svg>
        """u8.ToArray();

    public static IEndpointRouteBuilder MapMedia(this IEndpointRouteBuilder app)
    {
        var media = app.MapGroup("/media");
        media.MapGet("/thumb", Thumbnail);
        media.MapGet("/view", View);
        media.MapGet("/original", Original);
        return app;
    }

    /// <summary>URL of a media endpoint for a file of an album; <paramref name="version"/> changes when the file does.</summary>
    public static string Url(string endpoint, Guid albumId, string relativePath, long version) =>
        $"media/{endpoint}?album={albumId:N}&path={Uri.EscapeDataString(relativePath)}&v={version:x}";

    private static async Task<IResult> Thumbnail(HttpContext http, CoreServices core, MediaImages images, string? album, string? path)
    {
        if (Find(core, album, path) is not ({ } owner, { } file))
        {
            return Results.NotFound();
        }
        if (ThumbnailStore.Fresh(owner.DestinationRoot, file) is { } stored)
        {
            return Physical(http, stored, "image/jpeg", "s", LongCache);
        }
        var source = new FileInfo(file);
        if (MediaTypes.IsVideo(file))
        {
            return Bytes(http, VideoPlaceholder, "image/svg+xml", Tag("p", source), source, Revalidate);
        }
        var tag = Tag("t", source);
        if (IsNotModified(http, tag, LongCache))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        return await images.ThumbnailAsync(file, http.RequestAborted) is { } jpeg
            ? Bytes(http, jpeg, "image/jpeg", tag, source, LongCache)
            : Results.NotFound();
    }

    private static async Task<IResult> View(HttpContext http, CoreServices core, MediaImages images, string? album, string? path)
    {
        if (Find(core, album, path) is not (_, { } file) || MediaTypes.IsVideo(file))
        {
            return Results.NotFound();
        }
        if (MediaImages.BrowserShows(file))
        {
            return Physical(http, file, ContentType(file, inline: true), "o", LongCache);
        }
        var source = new FileInfo(file);
        var tag = Tag("v", source);
        if (IsNotModified(http, tag, LongCache))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        return await images.PreviewAsync(file, http.RequestAborted) is { } jpeg
            ? Bytes(http, jpeg, "image/jpeg", tag, source, LongCache)
            : Results.NotFound();
    }

    private static IResult Original(HttpContext http, CoreServices core, string? album, string? path, bool download = false)
    {
        if (Find(core, album, path) is not (_, { } file))
        {
            return Results.NotFound();
        }
        return Physical(http, file, ContentType(file, inline: !download), "o", LongCache, download ? Path.GetFileName(file) : null);
    }

    private static (Album Album, string File)? Find(CoreServices core, string? album, string? path) =>
        Guid.TryParse(album, out var id) && core.Albums.Get(id) is { } owner && AlbumMediaPath.Resolve(owner.DestinationRoot, path) is { } file
            ? (owner, file)
            : null;

    private static IResult Physical(HttpContext http, string file, string contentType, string kind, string cache, string? downloadName = null)
    {
        var info = new FileInfo(file);
        http.Response.Headers.CacheControl = cache;
        return TypedResults.PhysicalFile(file, contentType, downloadName, info.LastWriteTimeUtc, Tag(kind, info), enableRangeProcessing: true);
    }

    private static IResult Bytes(HttpContext http, byte[] content, string contentType, EntityTagHeaderValue tag, FileInfo source, string cache)
    {
        http.Response.Headers.CacheControl = cache;
        return TypedResults.File(content, contentType, lastModified: source.LastWriteTimeUtc, entityTag: tag);
    }

    /// <summary>Answers 304 before an expensive resize when the browser already has this version.</summary>
    private static bool IsNotModified(HttpContext http, EntityTagHeaderValue tag, string cache)
    {
        var matches = http.Request.GetTypedHeaders().IfNoneMatch;
        if (matches is null || !matches.Any(m => m.Equals(EntityTagHeaderValue.Any) || m.Compare(tag, useStrongComparison: false)))
        {
            return false;
        }
        http.Response.Headers.ETag = tag.ToString();
        http.Response.Headers.CacheControl = cache;
        return true;
    }

    private static EntityTagHeaderValue Tag(string kind, FileInfo file) =>
        new($"\"{kind}{file.LastWriteTimeUtc.Ticks:x}-{file.Length:x}\"");

    private static string ContentType(string file, bool inline)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();
        // QuickTime and M4V videos from phones are MPEG-4 inside: browsers play them when told so.
        if (inline && extension is ".mov" or ".m4v")
        {
            return "video/mp4";
        }
        return ContentTypes.TryGetContentType(file, out var type) ? type : "application/octet-stream";
    }

    private static FileExtensionContentTypeProvider CreateContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".heic"] = "image/heic";
        provider.Mappings[".heif"] = "image/heif";
        provider.Mappings[".dng"] = "image/x-adobe-dng";
        provider.Mappings[".3gp"] = "video/3gpp";
        provider.Mappings[".mkv"] = "video/x-matroska";
        provider.Mappings[".m4v"] = "video/x-m4v";
        return provider;
    }
}
