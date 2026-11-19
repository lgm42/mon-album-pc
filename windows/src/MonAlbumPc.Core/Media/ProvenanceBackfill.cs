using System.Diagnostics;
using System.Globalization;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Media;

/// <summary>A photo already in an album that would get its provenance keyword.</summary>
public sealed record ProvenanceCandidate(string Path, MediaSource Source)
{
    public string Keyword => Source.Keyword!;
}

/// <summary>A file the backfill could not mark, and why.</summary>
public sealed record ProvenanceProblem(string Path, string Reason);

/// <summary>Where the backfill stands: <paramref name="Step"/> is the text to show, <paramref name="File"/> the file at hand.</summary>
public readonly record struct ProvenanceProgress(string Step, int Done, int Total, string? File = null);

/// <summary>What marking the provenance of the existing files would do, worked out before touching anything.</summary>
public sealed record ProvenancePlan(IReadOnlyList<ProvenanceCandidate> Files, int AlreadyTagged, int OtherFormats)
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public int Count(MediaSourceKind kind) => Files.Count(f => f.Source.Kind == kind);

    /// <summary>
    /// "1 703 photos WhatsApp et 65 captures d'écran seront marquées ; les vidéos et les photos de l'appareil photo ne sont
    /// pas modifiées."
    /// </summary>
    public string Description
    {
        get
        {
            List<string> parts = [];
            AddPart(parts, Count(MediaSourceKind.WhatsApp), "photo WhatsApp", "photos WhatsApp");
            AddPart(parts, Count(MediaSourceKind.Screenshots), "capture d'écran", "captures d'écran");
            AddPart(parts, Count(MediaSourceKind.Other), "photo d'une autre application", "photos d'autres applications ou dossiers");
            var text = parts.Count == 0
                ? "Aucune photo à marquer"
                : $"{Join(parts)} {(Files.Count == 1 ? "sera marquée" : "seront marquées")}";
            text += " ; les vidéos et les photos de l'appareil photo ne sont pas modifiées.";
            if (AlreadyTagged > 0)
            {
                text += $" {Plural(AlreadyTagged, "photo est déjà marquée", "photos sont déjà marquées")}.";
            }
            if (OtherFormats > 0)
            {
                text += $" {Plural(OtherFormats, "image", "images")} dans un autre format que JPEG (PNG…) "
                        + $"{(OtherFormats == 1 ? "reste telle quelle" : "restent telles quelles")} : "
                        + "les marquer demanderait de les réécrire.";
            }
            return text;
        }
    }

    internal static string Plural(int count, string singular, string plural) =>
        $"{count.ToString("N0", French)} {(count is 0 or 1 ? singular : plural)}";

    private static void AddPart(List<string> parts, int count, string singular, string plural)
    {
        if (count > 0)
        {
            parts.Add(Plural(count, singular, plural));
        }
    }

    private static string Join(List<string> parts) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} et {parts[^1]}";
}

/// <summary>What the backfill did.</summary>
public sealed record ProvenanceSummary(int Tagged, int AlreadyTagged, IReadOnlyList<ProvenanceProblem> Problems, TimeSpan Duration)
{
    /// <summary>"1 703 photos marquées, 2 déjà marquées, 1 en échec."</summary>
    public string Description
    {
        get
        {
            var text = ProvenancePlan.Plural(Tagged, "photo marquée", "photos marquées");
            if (AlreadyTagged > 0)
            {
                text += $", {ProvenancePlan.Plural(AlreadyTagged, "déjà marquée", "déjà marquées")}";
            }
            if (Problems.Count > 0)
            {
                text += $", {ProvenancePlan.Plural(Problems.Count, "laissée telle quelle", "laissées telles quelles")}";
            }
            return text + ".";
        }
    }
}

/// <summary>
/// Marks the provenance of the photos already in the albums (imported before the keywords existed, or by hand), with
/// the same keywords as the import and without re-encoding anything. Provenance comes from the catalog, else from the
/// journals of the other servers, else from the file name. Running it again only marks what is still missing.
/// </summary>
public static class ProvenanceBackfill
{
    /// <summary>Lists what would be marked. Reads the start of the candidate photos to leave out those already marked.</summary>
    public static ProvenancePlan Plan(
        ImportHistory history, IEnumerable<Album> albums, IProgress<ProvenanceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<ProvenanceCandidate> candidates = [];
        var otherFormats = 0;
        foreach (var album in albums)
        {
            progress?.Report(new ProvenanceProgress($"Lecture du catalogue de l'album {album.Name}…", 0, 0));
            var provenance = AlbumProvenance.Load(history, album);
            progress?.Report(new ProvenanceProgress($"Liste des photos de l'album {album.Name}…", 0, 0));
            foreach (var item in PhotoLibrary.Scan(album))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.IsVideo || provenance.Of(item.Path) is not { Keyword: not null } source)
                {
                    continue;
                }
                if (IsJpeg(item.Path))
                {
                    candidates.Add(new ProvenanceCandidate(item.Path, source));
                }
                else
                {
                    otherFormats++;
                }
            }
        }

        List<ProvenanceCandidate> missing = [];
        var already = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[i];
            progress?.Report(new ProvenanceProgress("Vérification des photos déjà marquées", i, candidates.Count, Path.GetFileName(candidate.Path)));
            if (HasKeyword(candidate))
            {
                already++;
            }
            else
            {
                missing.Add(candidate);
            }
        }
        return new ProvenancePlan(missing, already, otherFormats);
    }

    /// <summary>
    /// Marks the photos of <paramref name="plan"/>. Each file is rewritten at once (temporary file, then replaced) and keeps
    /// its date, so thumbnails stay valid. Call it on a low-priority thread.
    /// </summary>
    public static ProvenanceSummary Run(
        ProvenancePlan plan, IProgress<ProvenanceProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        int tagged = 0, already = 0;
        List<ProvenanceProblem> problems = [];
        for (var i = 0; i < plan.Files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = plan.Files[i];
            progress?.Report(new ProvenanceProgress("Marquage", i, plan.Files.Count, Path.GetFileName(file.Path)));
            try
            {
                switch (XmpKeywords.Add(file.Path, file.Keyword))
                {
                    case KeywordOutcome.Added:
                        tagged++;
                        break;
                    case KeywordOutcome.AlreadyThere:
                        already++;
                        break;
                    default:
                        problems.Add(new ProvenanceProblem(file.Path, "métadonnées XMP illisibles ou JPEG inhabituel : non modifiée"));
                        break;
                }
            }
            catch (FileNotFoundException)
            {
                problems.Add(new ProvenanceProblem(file.Path, "fichier supprimé ou déplacé entre-temps"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add(new ProvenanceProblem(file.Path, e.Message));
            }
        }
        progress?.Report(new ProvenanceProgress("Marquage", plan.Files.Count, plan.Files.Count));
        return new ProvenanceSummary(tagged, already, problems, watch.Elapsed);
    }

    private static bool HasKeyword(ProvenanceCandidate candidate)
    {
        try
        {
            return XmpKeywords.Read(candidate.Path).Contains(candidate.Keyword, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false; // the run will say what is wrong with it
        }
    }

    private static bool IsJpeg(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg";
}
