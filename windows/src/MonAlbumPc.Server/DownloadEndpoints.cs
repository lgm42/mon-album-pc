using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MonAlbumPc.Core;

namespace MonAlbumPc.Server;

/// <summary>
/// Plain HTTP page opened when the pairing QR code is scanned with the phone camera (docs/protocol.md §7):
/// "open the app" if it is installed, otherwise download the APK.
/// </summary>
internal static class DownloadEndpoints
{
    private const string AndroidPackage = "io.github.lgm42.monalbumpc";
    private static readonly Lazy<string> Template = new(LoadTemplate);

    public static void Map(WebApplication app, CoreServices core, ServerOptions options)
    {
        app.MapGet("/", (HttpContext http) => Page(http, core, options, pairing: null));
        app.MapGet("/p", (HttpContext http) => Page(http, core, options, http.Request.QueryString.Value?.TrimStart('?')));
        app.MapGet("/monalbumpc.apk", () => options.Apk is { } apk && File.Exists(apk.Path)
            ? Results.File(apk.Path, "application/vnd.android.package-archive", "monalbumpc.apk")
            : Results.NotFound());
    }

    private static IResult Page(HttpContext http, CoreServices core, ServerOptions options, string? pairing)
    {
        var apkUrl = $"http://{http.Request.Host}/monalbumpc.apk";
        var openApp = pairing is { Length: > 0 }
            ? $"intent://pair?{pairing}#Intent;scheme=monalbumpc;package={AndroidPackage};" +
              $"S.browser_fallback_url={Uri.EscapeDataString(apkUrl)};end"
            : null;
        var hasApk = options.Apk is { } apk && File.Exists(apk.Path);

        var html = Template.Value
            .Replace("{{PC_NAME}}", WebUtility.HtmlEncode(core.PcName))
            .Replace("{{APK_URL}}", WebUtility.HtmlEncode(apkUrl))
            .Replace("{{OPEN_APP_URL}}", WebUtility.HtmlEncode(openApp ?? ""))
            .Replace("{{OPEN_APP_DISPLAY}}", openApp is null ? "none" : "flex")
            .Replace("{{APK_DISPLAY}}", hasApk ? "flex" : "none")
            .Replace("{{NO_APK_DISPLAY}}", hasApk ? "none" : "block");
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static string LoadTemplate()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonAlbumPc.Server.DownloadPage.html")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
