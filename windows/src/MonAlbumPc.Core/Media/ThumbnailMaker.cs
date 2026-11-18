using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using ImageMagick;
using ImageMagick.Formats;

namespace MonAlbumPc.Core.Media;

/// <summary>
/// Makes the thumbnails of <see cref="ThumbnailStore"/>: upright sRGB JPEGs fitting <see cref="ThumbnailStore.Size"/>,
/// without metadata. A video gives one of its frames through the ffmpeg program, when it is installed (the NAS image
/// has it; a Windows PC usually does not).
/// </summary>
public static class ThumbnailMaker
{
    public const uint Quality = 80;

    /// <summary>Where the frame of a video is taken: past the dark or blurry first frames. Shorter videos give their first one.</summary>
    public static readonly TimeSpan FramePosition = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan FfmpegTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The ffmpeg program found on the PATH, or null: videos then get no thumbnail.</summary>
    public static string? Ffmpeg { get; } = FindOnPath(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

    public static bool CanMake(string mediaPath) =>
        MediaTypes.IsPhoto(mediaPath) || (MediaTypes.IsVideo(mediaPath) && Ffmpeg is not null);

    /// <summary>
    /// Writes the thumbnail of <paramref name="mediaPath"/> and returns its path. It appears whole or not at all: a viewer
    /// may be reading the folder at the same time.
    /// </summary>
    public static string Make(string albumRoot, string mediaPath, CancellationToken cancellation = default)
    {
        var modified = File.GetLastWriteTimeUtc(mediaPath);
        using var image = MediaTypes.IsVideo(mediaPath) ? ReadFrame(mediaPath, cancellation) : ReadPhoto(mediaPath);
        Shrink(image);

        var thumbnail = ThumbnailStore.PathFor(albumRoot, mediaPath);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnail)!);
        var temp = $"{thumbnail}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = File.Create(temp))
            {
                image.Write(stream, MagickFormat.Jpeg);
            }
            // A file dated in the future (phone clock off) would otherwise never have a fresh thumbnail.
            if (modified > File.GetLastWriteTimeUtc(temp))
            {
                File.SetLastWriteTimeUtc(temp, modified);
            }
            File.Move(temp, thumbnail, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
        return thumbnail;
    }

    private static MagickImage ReadPhoto(string path)
    {
        var settings = new MagickReadSettings();
        // libjpeg decodes straight at a fraction of the size: far faster than decoding 12 Mpx to keep 0.3.
        settings.SetDefines(new JpegReadDefines { Size = new MagickGeometry(ThumbnailStore.Size * 2, ThumbnailStore.Size * 2) });
        var image = new MagickImage(path, settings);
        if (MediaTypes.NeedsJpegConversion(path))
        {
            // libheif already applies the HEIF rotation while decoding: the pixels are upright (see JpegConverter).
            image.Orientation = Orientation.TopLeft;
        }
        else
        {
            image.AutoOrient();
        }
        return image;
    }

    private static void Shrink(MagickImage image)
    {
        if (image.GetColorProfile() is not null)
        {
            image.TransformColorSpace(ColorProfiles.SRGB);
        }
        else if (image.ColorSpace == ColorSpace.CMYK)
        {
            image.ColorSpace = ColorSpace.sRGB;
        }
        if (image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }
        image.Thumbnail(new MagickGeometry(ThumbnailStore.Size, ThumbnailStore.Size) { Greater = true });
        image.Strip();
        image.Quality = Quality;
    }

    private static MagickImage ReadFrame(string path, CancellationToken cancellation)
    {
        var ffmpeg = Ffmpeg ?? throw new NotSupportedException("ffmpeg est introuvable : pas de miniature pour les vidéos.");
        var (frame, error) = Frame(ffmpeg, path, FramePosition, cancellation);
        if (frame is null)
        {
            // Shorter than FramePosition: nothing came out.
            (frame, error) = Frame(ffmpeg, path, TimeSpan.Zero, cancellation);
        }
        return frame is not null ? new MagickImage(frame)
            : throw new InvalidDataException($"Aucune image lisible dans la vidéo{(error.Length > 0 ? $" : {error}" : "")}");
    }

    /// <summary>A PNG of the frame at <paramref name="at"/>, already upright and fitting the thumbnail, or null with ffmpeg's errors.</summary>
    private static (byte[]? Png, string Error) Frame(string ffmpeg, string path, TimeSpan at, CancellationToken cancellation)
    {
        var size = ThumbnailStore.Size.ToString(CultureInfo.InvariantCulture);
        var start = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string[] arguments =
        [
            "-hide_banner", "-loglevel", "error", "-nostdin", "-threads", "1",
            "-ss", at.TotalSeconds.ToString(CultureInfo.InvariantCulture), "-i", path,
            "-an", "-sn", "-dn", "-frames:v", "1",
            "-vf", $"scale='min({size},iw)':'min({size},ih)':force_original_aspect_ratio=decrease",
            "-f", "image2pipe", "-c:v", "png", "-",
        ];
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidDataException("ffmpeg n'a pas démarré.");
        try
        {
            process.PriorityClass = ProcessPriorityClass.Idle;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // Already finished, or not allowed: it still runs at the priority of the thread that started it.
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(FfmpegTimeout);
        var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, limit.Token);
        var errors = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            Task.WhenAll(copy, errors, process.WaitForExitAsync(limit.Token)).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            cancellation.ThrowIfCancellationRequested();
            throw new TimeoutException($"ffmpeg n'a pas répondu en {FfmpegTimeout.TotalMinutes:0} minutes.");
        }
        return (process.ExitCode == 0 && output.Length > 0 ? output.ToArray() : null, errors.Result.Trim());
    }

    private static string? FindOnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, program))
            .FirstOrDefault(File.Exists);
}
