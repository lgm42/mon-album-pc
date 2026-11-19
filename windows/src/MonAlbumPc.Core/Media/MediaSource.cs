using System.Text.RegularExpressions;

namespace MonAlbumPc.Core.Media;

public enum MediaSourceKind { Camera, WhatsApp, Screenshots, Other }

/// <summary>
/// Where a photo or video comes from on the phone: the camera, WhatsApp, a screenshot, or another app or folder
/// (<see cref="Folder"/>, as the phone named it). Known from the folder the phone sent it from, else guessed from the file
/// name for files imported before that folder was kept.
/// </summary>
public sealed partial record MediaSource(MediaSourceKind Kind, string? Folder = null)
{
    public static readonly MediaSource Unknown = new(MediaSourceKind.Other);

    /// <summary>What the viewers show: the category, or the folder name for other apps and folders.</summary>
    public string Label => Kind == MediaSourceKind.Other && !string.IsNullOrWhiteSpace(Folder) ? Folder : KindLabel(Kind);

    /// <summary>
    /// Keyword written into the photos that do not come from the camera, or null for none (camera, or nothing to say).
    /// </summary>
    public string? Keyword => Kind switch
    {
        MediaSourceKind.WhatsApp => "WhatsApp",
        MediaSourceKind.Screenshots => "Capture d'écran",
        MediaSourceKind.Other when !string.IsNullOrWhiteSpace(Folder) => Folder,
        _ => null,
    };

    public static string KindLabel(MediaSourceKind kind) => kind switch
    {
        MediaSourceKind.Camera => "Appareil photo",
        MediaSourceKind.WhatsApp => "WhatsApp",
        MediaSourceKind.Screenshots => "Captures d'écran",
        _ => "Autres",
    };

    /// <summary>
    /// Provenance of <paramref name="fileName"/> sent from <paramref name="sourceFolder"/> (the phone's folder name, such
    /// as "WhatsApp Images" or "Appareil photo", or a path such as "DCIM/Camera"), or from its name alone when unknown.
    /// </summary>
    public static MediaSource Classify(string? sourceFolder, string fileName)
    {
        var segments = (sourceFolder ?? "").Split('/', '\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return FromName(Path.GetFileName(fileName));
        }
        var folder = segments[^1];
        if (segments.Any(s => s.Contains("whatsapp", StringComparison.OrdinalIgnoreCase)))
        {
            return new MediaSource(MediaSourceKind.WhatsApp, folder);
        }
        var key = folder.ToLowerInvariant().Replace('’', '\'');
        if (key is "appareil photo" or "camera" or "dcim" or "opencamera" || DcfFolder().IsMatch(folder))
        {
            return new MediaSource(MediaSourceKind.Camera, folder);
        }
        if (key is "captures d'écran" or "capture d'écran" or "screenshots" or "screenshot")
        {
            return new MediaSource(MediaSourceKind.Screenshots, folder);
        }
        return new MediaSource(MediaSourceKind.Other, folder);
    }

    /// <summary>The folder part of an import's source path ("WhatsApp Images/IMG-….jpg" → "WhatsApp Images").</summary>
    public static string? FolderOf(string? sourcePath)
    {
        var slash = sourcePath?.LastIndexOfAny(['/', '\\']) ?? -1;
        return slash > 0 ? sourcePath![..slash] : null;
    }

    private static MediaSource FromName(string name)
    {
        if (WhatsAppName().IsMatch(name))
        {
            return new MediaSource(MediaSourceKind.WhatsApp);
        }
        if (ScreenshotName().IsMatch(name))
        {
            return new MediaSource(MediaSourceKind.Screenshots);
        }
        return CameraName().IsMatch(name) ? new MediaSource(MediaSourceKind.Camera) : Unknown;
    }

    // IMG-20231126-WA0007.jpg, VID-20231126-WA0002.mp4, STK-…-WA….webp
    [GeneratedRegex(@"^(IMG|VID|AUD|PTT|STK|DOC)-\d{8}-WA\d+", RegexOptions.IgnoreCase)]
    private static partial Regex WhatsAppName();

    [GeneratedRegex(@"^(Screenshot|Capture d.écran)", RegexOptions.IgnoreCase)]
    private static partial Regex ScreenshotName();

    // IMG_20231126_101010.jpg, PXL_…, VID_…, 20231126_101010.jpg (Samsung), DSC_0001.JPG
    [GeneratedRegex(@"^((IMG|VID|PXL|MVIMG|MVI|DSC[NF]?|GOPR)_?\d|\d{8}_\d{6})", RegexOptions.IgnoreCase)]
    private static partial Regex CameraName();

    // Camera folders named by the DCF standard: DCIM/100ANDRO, DCIM/101MSDCF.
    [GeneratedRegex(@"^[1-9]\d{2}[A-Za-z0-9_]{5}$")]
    private static partial Regex DcfFolder();
}
