using System.Text.Json;
using MonAlbumPc.Server;

namespace MonAlbumPc.App;

/// <summary>
/// The Android app shipped next to the Windows app: <c>Android\monalbumpc.apk</c> and its
/// <c>monalbumpc.apk.json</c> (version), as published by CI (<c>tools/fetch-apk.ps1</c> in development).
/// </summary>
internal static class ApkLocator
{
    public static ApkFile? Find()
    {
        var apk = Path.Combine(AppContext.BaseDirectory, "Android", "monalbumpc.apk");
        var manifest = apk + ".json";
        if (!File.Exists(apk) || !File.Exists(manifest))
        {
            return null;
        }
        try
        {
            var version = JsonSerializer.Deserialize<ApkVersion>(File.ReadAllText(manifest), JsonOptions)!;
            return new ApkFile(apk, version.VersionCode, version.VersionName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record ApkVersion(int VersionCode, string VersionName);
}
