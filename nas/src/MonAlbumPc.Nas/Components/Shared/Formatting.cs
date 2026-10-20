using System.Globalization;

namespace MonAlbumPc.Nas.Components.Shared;

/// <summary>French formatting shared by the pages (same rules as the Windows app).</summary>
public static class Formatting
{
    public static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static string Count(long value) => value.ToString("N0", French);

    public static string Plural(long count, string singular, string plural) =>
        $"{Count(count)} {(count is 0 or 1 ? singular : plural)}";

    /// <summary>"à l'instant", "il y a 5 min", "il y a 2 h", "hier", "il y a 9 jours"…</summary>
    public static string Ago(DateTimeOffset? when, DateTimeOffset now)
    {
        if (when is null)
        {
            return "jamais";
        }
        var elapsed = now - when.Value;
        if (elapsed < TimeSpan.FromMinutes(1)) return "à l'instant";
        if (elapsed < TimeSpan.FromHours(1)) return $"il y a {(int)elapsed.TotalMinutes} min";
        if (when.Value.LocalDateTime.Date == now.LocalDateTime.Date) return $"il y a {(int)elapsed.TotalHours} h";
        if (when.Value.LocalDateTime.Date == now.LocalDateTime.Date.AddDays(-1)) return "hier";
        return $"il y a {(int)Math.Ceiling((now.LocalDateTime.Date - when.Value.LocalDateTime.Date).TotalDays)} jours";
    }

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", French) + " Go",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", French) + " Mo",
        _ => $"{Math.Max(1, bytes / 1024)} Ko",
    };

    public static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], French) + text[1..];

    /// <summary>"Octobre 2026".</summary>
    public static string Month(DateTime month) => Capitalize(month.ToString("MMMM yyyy", French));

    /// <summary>First letter of an album name, for its avatar.</summary>
    public static string Initial(string name) => name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
}
