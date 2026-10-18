using System.Globalization;

namespace MonAlbumPc.App;

internal static class Formatting
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

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
}
