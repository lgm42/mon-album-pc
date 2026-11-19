using System.Diagnostics;
using MonAlbumPc.Core.Activity;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tags;

/// <summary>A file to tag: its album and its full path.</summary>
public sealed record TagTarget(Album Album, string Path);

/// <summary>A file a tag operation could not change, and why.</summary>
public sealed record TagFailure(string Path, string Reason)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>What a tag operation did: <see cref="Summary"/> is the sentence shown at the end.</summary>
public sealed record TagRunResult(string Summary, int Changed, int Unchanged, IReadOnlyList<TagFailure> Failures);

/// <summary>
/// Adds, removes, creates, renames, merges and deletes the user tags of the library. Files are rewritten one at a time on
/// a low-priority thread, one operation after the other, each shown on the <see cref="WorkBoard"/> with its progress; every
/// file written updates the index at once and the tag journal of its album, for the other servers.
/// </summary>
public sealed class TagService(AlbumStore albums, ImportHistory history, TagCatalog catalog, TagJournals journals, WorkBoard work)
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public const string Title = "Tags";

    private readonly SemaphoreSlim _queue = new(1, 1);
    private Timer? _poll;

    /// <summary>Raised from a background thread when tags of the library changed (here or on another server).</summary>
    public event Action? Changed;

    /// <summary>Every tag with its number of photos, those created ahead included (0).</summary>
    public List<TagCount> List() => catalog.List();

    /// <summary>The tags of every tagged file, by album and relative path ('/' separators, any case).</summary>
    public Dictionary<(Guid AlbumId, string Path), IReadOnlyList<string>> ByFile() =>
        catalog.Files().ToDictionary(f => (f.AlbumId, f.Path), f => f.Tags, FileKeyComparer.Instance);

    /// <summary>The number of files carrying <paramref name="tag"/> or a tag under it, counted now.</summary>
    public int CountWithin(string tag) => catalog.FilesWithin(tag).Count;

    /// <summary>The tags under <paramref name="tag"/>, itself left out.</summary>
    public List<string> Children(string tag) =>
        [.. catalog.List().Select(t => t.Name).Where(n => UserTags.IsWithin(n, tag) && !UserTags.Same.Equals(n, tag))];

    /// <summary>The spelling the library already uses for a typed tag, tidied; null when nothing is left.</summary>
    public string? Canonical(string? typed) => UserTags.Normalize(typed) is { } tag ? catalog.Canonical(tag) : null;

    /// <summary>True when the library has this tag (on photos or created ahead).</summary>
    public bool Exists(string tag) => catalog.List().Any(t => UserTags.Same.Equals(t.Name, tag));

    /// <summary>Looks at the tag journals of the other servers now, then every <see cref="PollInterval"/>.</summary>
    public void StartPolling() => _poll ??= new Timer(_ => Poll(), null, TimeSpan.Zero, PollInterval);

    public void StopPolling()
    {
        _poll?.Dispose();
        _poll = null;
    }

    /// <summary>Takes the tags written by the other servers. Never call it on a UI thread: albums may be network shares.</summary>
    public int Poll()
    {
        try
        {
            var changes = journals.SyncAll();
            if (changes > 0)
            {
                Changed?.Invoke();
            }
            return changes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            return 0; // looked at again next time
        }
    }

    /// <summary>
    /// Adds a tag to the list before any photo carries it, for this server and the others (written to every album
    /// folder: off the UI thread). Returns its name, or null when there was nothing to create.
    /// </summary>
    public Task<string?> CreateAsync(string typed) => Task.Run(() =>
    {
        if (Canonical(typed) is not { } name || Exists(name))
        {
            return Canonical(typed);
        }
        journals.Declare(name, declared: true, Now());
        work.Info($"Tag « {name} » créé");
        Changed?.Invoke();
        return name;
    });

    /// <summary>Adds a tag to files; a tag not known yet is created.</summary>
    public Task<TagRunResult> AddAsync(IReadOnlyList<TagTarget> files, string typed)
    {
        var name = Canonical(typed) ?? throw new ArgumentException("Tag vide.", nameof(typed));
        return RunAsync($"Ajout du tag « {name} »", () => files, _ => ([name], []),
            n => $"Tag « {name} » ajouté à {WorkText.Plural(n, "photo", "photos")}",
            n => WorkText.Plural(n, "l'avait déjà", "l'avaient déjà"));
    }

    public Task<TagRunResult> RemoveAsync(IReadOnlyList<TagTarget> files, string tag) =>
        RunAsync($"Retrait du tag « {tag} »", () => files, _ => ([], [tag]),
            n => $"Tag « {tag} » retiré de {WorkText.Plural(n, "photo", "photos")}",
            n => WorkText.Plural(n, "ne l'avait pas", "ne l'avaient pas"));

    /// <summary>
    /// Renames <paramref name="from"/> and the tags under it ("Bricolage/ESP32" follows "Bricolage"); into an existing tag,
    /// this merges them.
    /// </summary>
    public Task<TagRunResult> RenameAsync(string from, string to)
    {
        var name = Canonical(to) ?? throw new ArgumentException("Tag vide.", nameof(to));
        var merge = !UserTags.Same.Equals(from, name) && Exists(name);
        string Map(string tag) => name + tag[from.Length..];
        return RunAsync(merge ? $"Fusion du tag « {from} » dans « {name} »" : $"Renommage du tag « {from} » en « {name} »",
            () =>
            {
                // Those created ahead without photos only live in the list: renamed there.
                foreach (var tag in catalog.List().Where(t => UserTags.IsWithin(t.Name, from)))
                {
                    journals.Declare(tag.Name, declared: false, Now());
                    if (tag.Count == 0)
                    {
                        journals.Declare(Map(tag.Name), declared: true, Now());
                    }
                }
                return Targets(from);
            },
            tags =>
            {
                var old = tags.Where(t => UserTags.IsWithin(t, from)).ToList();
                return ([.. old.Select(Map)], old);
            },
            n => merge
                ? $"Tag « {from} » fusionné dans « {name} » sur {WorkText.Plural(n, "photo", "photos")}"
                : $"Tag « {from} » renommé en « {name} » sur {WorkText.Plural(n, "photo", "photos")}",
            null);
    }

    /// <summary>Removes <paramref name="tag"/> and the tags under it from every file, and from the list.</summary>
    public Task<TagRunResult> DeleteAsync(string tag) =>
        RunAsync($"Suppression du tag « {tag} »",
            () =>
            {
                foreach (var declared in catalog.List().Where(t => UserTags.IsWithin(t.Name, tag)))
                {
                    journals.Declare(declared.Name, declared: false, Now());
                }
                return Targets(tag);
            },
            tags => ([], [.. tags.Where(t => UserTags.IsWithin(t, tag))]),
            n => $"Tag « {tag} » supprimé de {WorkText.Plural(n, "photo", "photos")}",
            null);

    private List<TagTarget> Targets(string tag)
    {
        var byId = albums.List().ToDictionary(a => a.Id);
        return [.. catalog.FilesWithin(tag)
            .Where(f => byId.ContainsKey(f.AlbumId))
            .Select(f => new TagTarget(byId[f.AlbumId],
                System.IO.Path.Combine(byId[f.AlbumId].DestinationRoot, f.Path.Replace('/', System.IO.Path.DirectorySeparatorChar))))];
    }

    /// <summary>
    /// Runs one operation on its own low-priority thread, after those already queued. <paramref name="change"/> gives the
    /// tags to add and remove from the current tags of each file.
    /// </summary>
    private Task<TagRunResult> RunAsync(string title, Func<IReadOnlyList<TagTarget>> targets,
        Func<IReadOnlyList<string>, (List<string> Add, List<string> Remove)> change, Func<int, string> summary, Func<int, string>? unchangedText)
    {
        var job = work.Start(title, "En attente des opérations en cours");
        var done = new TaskCompletionSource<TagRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            _queue.Wait();
            try
            {
                job.Step("Préparation");
                var files = targets();
                job.Step($"{WorkText.Plural(files.Count, "fichier", "fichiers")} à modifier", files.Count);
                done.SetResult(Run(job, files, change, summary, unchangedText));
            }
            catch (Exception e)
            {
                // Out of a background thread, an exception would close the whole app.
                job.Finish($"{title} interrompu : {e.Message}", WorkLevel.Error, error: e);
                done.SetException(e);
            }
            finally
            {
                _queue.Release();
                Changed?.Invoke();
            }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = Title }.Start();
        return done.Task;
    }

    private TagRunResult Run(WorkJob job, IReadOnlyList<TagTarget> files,
        Func<IReadOnlyList<string>, (List<string> Add, List<string> Remove)> change, Func<int, string> summary, Func<int, string>? unchangedText)
    {
        var watch = Stopwatch.StartNew();
        var provenances = new Dictionary<Guid, AlbumProvenance>();
        int changed = 0, unchanged = 0;
        List<TagFailure> failures = [];
        for (var i = 0; i < files.Count; i++)
        {
            var (album, path) = files[i];
            job.Progress(i, files.Count, System.IO.Path.GetFileName(path));
            try
            {
                if (!provenances.TryGetValue(album.Id, out var provenance))
                {
                    provenance = provenances[album.Id] = AlbumProvenance.Load(history, album);
                }
                var keep = provenance.Of(path).Keyword;
                // From the file itself: the index may not have seen its latest tags yet.
                var (add, remove) = change(UserTags.Read(path, keep));
                var written = UserTags.Change(path, add, remove, keep);
                if (!written.Changed)
                {
                    unchanged++;
                    continue;
                }
                changed++;
                var at = Now();
                var relative = AlbumMediaPath.Relative(album.DestinationRoot, path);
                catalog.Set(album.Id, relative, path, written.Tags, at);
                journals.Append(album, new TagJournalEntry(at, relative, written.Tags, new FileInfo(path).Length));
            }
            catch (FileNotFoundException)
            {
                Fail(path, "fichier supprimé ou déplacé entre-temps");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
            {
                Fail(path, e.Message);
            }
        }
        job.Progress(files.Count, files.Count);

        var text = summary(changed);
        if (unchanged > 0 && unchangedText is not null)
        {
            text += $", {unchangedText(unchanged)}";
        }
        if (failures.Count > 0)
        {
            text += $", {WorkText.Count(failures.Count)} en échec";
        }
        text += $" en {WorkText.Duration(watch.Elapsed)}";
        job.Finish(text, failures.Count > 0 ? WorkLevel.Warning : WorkLevel.Info);
        return new TagRunResult(text, changed, unchanged, failures);

        void Fail(string path, string reason)
        {
            failures.Add(new TagFailure(path, reason));
            job.Warn($"{job.Title} : {System.IO.Path.GetFileName(path)} laissé tel quel ({reason})");
        }
    }

    private DateTimeOffset Now() => work.Clock.GetUtcNow();

    /// <summary>Album and relative path, the path ignoring case as the album folders do.</summary>
    private sealed class FileKeyComparer : IEqualityComparer<(Guid AlbumId, string Path)>
    {
        public static readonly FileKeyComparer Instance = new();

        public bool Equals((Guid AlbumId, string Path) x, (Guid AlbumId, string Path) y) =>
            x.AlbumId == y.AlbumId && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid AlbumId, string Path) key) =>
            HashCode.Combine(key.AlbumId, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Path));
    }
}
