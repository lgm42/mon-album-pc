using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;
using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Nas;

/// <summary>
/// Files for the web viewer: cached thumbnails (photos with Magick, videos with ffmpeg when installed) and the
/// original files, with range requests so videos can be seeked. Only paths under the photos root are served.
/// </summary>
public static class MediaEndpoints
{
    private static readonly SemaphoreSlim Gate = new(Math.Max(2, Environment.ProcessorCount / 2));

    public static void Map(WebApplication app, NasOptions options)
    {
        var cache = Path.Combine(options.ConfigDirectory, "thumbnails");

        app.MapGet("/media/file", (string path) =>
            Allowed(options, path) ? Results.File(path, ContentType(path), enableRangeProcessing: true) : Results.NotFound());

        app.MapGet("/media/thumb", async (string path, int? size) =>
        {
            if (!Allowed(options, path))
            {
                return Results.NotFound();
            }
            var pixels = Math.Clamp(size ?? 320, 64, 1024);
            var file = new FileInfo(path);
            var key = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes($"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{pixels}")));
            var cached = Path.Combine(cache, key[..2], key + ".jpg");
            if (!File.Exists(cached))
            {
                await Gate.WaitAsync();
                try
                {
                    if (!File.Exists(cached) && !await RenderAsync(file.FullName, cached, pixels))
                    {
                        return Results.NotFound();
                    }
                }
                finally
                {
                    Gate.Release();
                }
            }
            return Results.File(cached, "image/jpeg");
        });
    }

    private static bool Allowed(NasOptions options, string path) =>
        !string.IsNullOrEmpty(path) && options.IsUnderPhotos(path) && File.Exists(path) && MediaTypes.IsMedia(path);

    private static async Task<bool> RenderAsync(string source, string target, int pixels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".tmp";
        try
        {
            if (MediaTypes.IsVideo(source))
            {
                if (!await VideoFrameAsync(source, temp, pixels))
                {
                    return false;
                }
            }
            else
            {
                using var image = new MagickImage(source);
                image.AutoOrient(); // phones store portrait photos sideways with an EXIF orientation
                image.Thumbnail(new MagickGeometry((uint)pixels, (uint)pixels) { FillArea = true });
                image.Crop((uint)pixels, (uint)pixels, Gravity.Center);
                image.Strip();
                image.Quality = 82;
                image.Format = MagickFormat.Jpeg;
                await image.WriteAsync(temp);
            }
            File.Move(temp, target, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is MagickException or IOException)
        {
            File.Delete(temp);
            return false;
        }
    }

    /// <summary>A frame one second in, scaled and cropped to a square. Needs ffmpeg (installed in the image).</summary>
    private static async Task<bool> VideoFrameAsync(string source, string target, int pixels)
    {
        var start = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
                 {
                     "-y", "-loglevel", "error", "-ss", "1", "-i", source, "-frames:v", "1",
                     "-vf", $"scale={pixels}:{pixels}:force_original_aspect_ratio=increase,crop={pixels}:{pixels}",
                     "-f", "image2", "-c:v", "mjpeg", target,
                 })
        {
            start.ArgumentList.Add(argument);
        }
        try
        {
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync();
            return process.ExitCode == 0 && File.Exists(target);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // ffmpeg missing
        }
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".heic" or ".heif" => "image/heic",
        ".mp4" or ".m4v" => "video/mp4",
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        ".mkv" => "video/x-matroska",
        ".3gp" => "video/3gpp",
        _ => "application/octet-stream",
    };
}
