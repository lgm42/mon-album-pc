using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Library;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Tags;

namespace MonAlbumPc.App.Photos;

/// <summary>Viewer: filters by album and month, virtualized month-grouped grid, information pane, full screen.</summary>
public partial class PhotosPage : UserControl, IRefreshable
{
    private const double Gap = 4;
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly CoreServices _core;
    private readonly MainWindow _window;
    private List<Album> _albums = [];
    private List<LibraryItem> _all = [];
    private Dictionary<string, MediaSource> _sources = [];
    private List<PhotoTile> _tiles = [];
    private List<string> _paths = [];
    private Dictionary<string, PhotoTile> _byPath = [];
    // By path, so that the selection outlives the tiles rebuilt by a refresh.
    private readonly Selection<string> _selection = new();
    // A deletion or an emptying of the trash is running.
    private bool _busy;
    private List<TrashContent> _trashes = [];
    private int _trashMeasure;
    private Guid? _albumFilter;
    private DateTime? _monthFilter;
    private int? _yearFilter;
    private MediaSourceKind? _sourceFilter;
    private int _columns;
    private bool _updatingFilters;

    // Tags of the files by full path, and the tags of the library (those without photos too).
    private Dictionary<string, IReadOnlyList<string>> _tags = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<TagCount> _library = [];
    private readonly TagSelection _tagFilter = new();

    public PhotosPage(CoreServices core, MainWindow window)
    {
        _core = core;
        _window = window;
        InitializeComponent();
        UpdateCommands();
        InfoTags.Added += tag => ApplyTag(SelectedTiles().Select(t => t.Item).ToList(), tag, add: true);
        InfoTags.Removed += tag => ApplyTag(SelectedTiles().Select(t => t.Item).ToList(), tag, add: false);
        ViewerTags.Added += tag => ApplyTag(FocusedTile is { } tile ? [tile.Item] : [], tag, add: true);
        ViewerTags.Removed += tag => ApplyTag(FocusedTile is { } tile ? [tile.Item] : [], tag, add: false);
        AddTagBox.Chosen += tag =>
        {
            AddTagPopup.IsOpen = false;
            ApplyTag(SelectedTiles().Select(t => t.Item).ToList(), tag, add: true);
        };
        AddTagBox.Cancelled += () => AddTagPopup.IsOpen = false;
        // Tags set elsewhere (the NAS, another viewer, the Tags page): raised from a background thread.
        _core.Tags.Changed += () => Dispatcher.BeginInvoke(ReloadTags);
    }

    /// <summary>Shows the photos carrying <paramref name="tag"/> (from the Tags page).</summary>
    public void ShowTag(string tag)
    {
        _tagFilter.Clear();
        _tagFilter.Tags.Add(tag);
        BuildFilters();
        BuildTiles();
    }

    public async void Refresh()
    {
        if (_window.Library is { State: LibraryState.Unavailable } && _all.Count > 0)
        {
            return; // the share does not answer: what is on screen is better than an empty page
        }
        _albums = _core.Albums.List();
        var albums = _albums;
        if (_all.Count == 0)
        {
            Caption.Text = "Lecture des albums et des provenances…";
        }
        // Walking big album folders takes a while, as reading the journals of a NAS album: keep the window responsive.
        var (all, sources, tags) = await Task.Run(() =>
        {
            var sources = new Dictionary<string, MediaSource>(StringComparer.OrdinalIgnoreCase);
            var items = albums.SelectMany(album =>
            {
                var provenance = AlbumProvenance.Load(_core.History, album);
                var scanned = PhotoLibrary.Scan(album);
                foreach (var item in scanned)
                {
                    sources[item.Path] = provenance.Of(item.Path);
                }
                return scanned;
            }).ToList();
            return (items, sources, LoadTags(albums));
        });
        _all = all;
        _sources = sources;
        (_tags, _library) = tags;
        BuildFilters();
        BuildTiles();
        UpdateTrash();
    }

    // ---- Filters ----

    private void BuildFilters()
    {
        _updatingFilters = true;
        var albums = new List<FilterEntry> { new("Tous les albums", _all.Count, null, null, null, false) };
        albums.AddRange(_albums.Select(a => new FilterEntry(a.Name, _all.Count(i => i.AlbumId == a.Id),
            (Brush)new BrushConverter().ConvertFromString(a.Color)!, a.Id, null, false)));
        AlbumList.ItemsSource = albums;
        AlbumList.SelectedItem = albums.FirstOrDefault(a => a.AlbumId == _albumFilter) ?? albums[0];
        _albumFilter = ((FilterEntry)AlbumList.SelectedItem).AlbumId;
        _updatingFilters = false;
        BuildSourceFilters();
        BuildDateFilters();
        BuildTagFilters();
    }

    /// <summary>The tags as a tree, counted in the album, period and provenance chosen, with « Sans tag ».</summary>
    private void BuildTagFilters()
    {
        var items = InAlbum().Where(InPeriod).Where(InSource).ToList();
        var entries = TagSelection.Entries(_library.Select(t => t.Name), items.Select(TagsOf));
        _tagFilter.Retain(entries.Select(e => e.Name));
        var search = TagSearch.Text.Trim();
        var searching = search.Length > 0;
        List<TagFilterRow> rows = [.. entries
            .Where(e => !searching || French.CompareInfo.IndexOf(e.Name, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            .Select(e => new TagFilterRow(e.Name, searching ? e.Name : e.Leaf, e.Count, searching ? 0 : e.Depth, _tagFilter.Tags.Contains(e.Name)))];
        if (!searching)
        {
            rows.Add(new TagFilterRow("", "Sans tag", items.Count(i => TagsOf(i).Count == 0), 0, _tagFilter.Untagged, IsUntagged: true));
        }
        TagFilterList.ItemsSource = rows;
        var typed = UserTags.Normalize(search);
        CreateTagText.Text = $"+  Créer le tag « {typed} »";
        CreateTagButton.Visibility = typed is not null && !entries.Any(e => UserTags.Same.Equals(e.Name, typed)) ? Visibility.Visible : Visibility.Collapsed;
        NoTagHint.Visibility = entries.Count == 0 && !searching ? Visibility.Visible : Visibility.Collapsed;
        ClearTagsButton.Visibility = _tagFilter.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        TagSearchHint.Visibility = TagSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Toutes, Appareil photo, WhatsApp…, counted in the album, period and tags chosen.</summary>
    private void BuildSourceFilters()
    {
        _updatingFilters = true;
        var items = InAlbum().Where(InPeriod).Where(InTags).ToList();
        var sources = new List<FilterEntry> { new("Toutes", items.Count, null, null, null, false) };
        sources.AddRange(Enum.GetValues<MediaSourceKind>().Select(kind =>
            new FilterEntry(MediaSource.KindLabel(kind), items.Count(i => SourceOf(i).Kind == kind), null, null, null, false, kind)));
        SourceList.ItemsSource = sources;
        SourceList.SelectedItem = sources.FirstOrDefault(s => s.Source == _sourceFilter) ?? sources[0];
        _updatingFilters = false;
    }

    /// <summary>Years, each followed by its months, counted in the album and provenance chosen.</summary>
    private void BuildDateFilters()
    {
        _updatingFilters = true;
        var dates = new List<FilterEntry>();
        foreach (var year in InAlbum().Where(InSource).Where(InTags).GroupBy(i => i.Month.Year).OrderByDescending(g => g.Key))
        {
            dates.Add(new FilterEntry(year.Key.ToString(), year.Count(), null, null, new DateTime(year.Key, 1, 1), true));
            dates.AddRange(year.GroupBy(i => i.Month).OrderByDescending(g => g.Key).Select(m =>
                new FilterEntry("    " + Capitalize(m.Key.ToString("MMMM", French)), m.Count(), null, null, m.Key, false)));
        }
        DateList.ItemsSource = dates;
        DateList.SelectedItem = dates.FirstOrDefault(d => _monthFilter is not null && d.Month == _monthFilter && !d.IsYear)
                                ?? dates.FirstOrDefault(d => _yearFilter is not null && d.IsYear && d.Month?.Year == _yearFilter);
        _updatingFilters = false;
    }

    private IEnumerable<LibraryItem> InAlbum() => _all.Where(i => _albumFilter is null || i.AlbumId == _albumFilter);

    private bool InPeriod(LibraryItem item) =>
        (_monthFilter is null || item.Month == _monthFilter) && (_yearFilter is null || item.Month.Year == _yearFilter);

    private bool InSource(LibraryItem item) => _sourceFilter is null || SourceOf(item).Kind == _sourceFilter;

    private MediaSource SourceOf(LibraryItem item) =>
        _sources.GetValueOrDefault(item.Path) ?? MediaSource.Classify(null, item.Path);

    private IEnumerable<LibraryItem> Filtered() => InAlbum().Where(InPeriod).Where(InSource).Where(InTags);

    private bool InTags(LibraryItem item) => _tagFilter.Matches(TagsOf(item));

    private IReadOnlyList<string> TagsOf(LibraryItem item) => _tags.GetValueOrDefault(item.Path) ?? [];

    private void OnTagFilterClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TagFilterRow row)
        {
            return;
        }
        if (row.IsUntagged)
        {
            _tagFilter.Untagged = !_tagFilter.Untagged;
        }
        else
        {
            _tagFilter.Toggle(row.Name);
        }
        OnTagFilterChanged();
    }

    private void OnClearTagFilter(object sender, RoutedEventArgs e)
    {
        _tagFilter.Clear();
        OnTagFilterChanged();
    }

    private void OnTagFilterChanged()
    {
        BuildSourceFilters();
        BuildDateFilters();
        // The period chosen may have nothing with these tags: show them all rather than an empty grid.
        if (DateList.SelectedItem is null && (_monthFilter is not null || _yearFilter is not null))
        {
            _monthFilter = null;
            _yearFilter = null;
            BuildSourceFilters();
        }
        BuildTagFilters();
        BuildTiles();
    }

    private void OnTagSearch(object sender, TextChangedEventArgs e) => BuildTagFilters();

    private void OnTagSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            TagSearch.Text = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && TagFilterList.ItemsSource is List<TagFilterRow> rows)
        {
            var tags = rows.Where(r => !r.IsUntagged).ToList();
            if (tags is [var only])
            {
                TagSearch.Text = "";
                _tagFilter.Toggle(only.Name);
                OnTagFilterChanged();
            }
            else if (tags.Count == 0 && CreateTagButton.Visibility == Visibility.Visible)
            {
                OnCreateTagFromFilter(sender, e);
            }
            e.Handled = true;
        }
    }

    private async void OnCreateTagFromFilter(object sender, RoutedEventArgs e)
    {
        var typed = TagSearch.Text;
        TagSearch.Text = "";
        await _core.Tags.CreateAsync(typed);
        ReloadTags();
    }

    private void OnAlbumFilter(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFilters || AlbumList.SelectedItem is not FilterEntry entry)
        {
            return;
        }
        _albumFilter = entry.AlbumId;
        _monthFilter = null;
        _yearFilter = null;
        BuildFilters();
        BuildTiles();
        UpdateTrash();
    }

    private void OnDateFilter(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFilters)
        {
            return;
        }
        var entry = DateList.SelectedItem as FilterEntry;
        _yearFilter = entry is { IsYear: true } ? entry.Month!.Value.Year : null;
        _monthFilter = entry is { IsYear: false } ? entry.Month : null;
        BuildSourceFilters();
        BuildTagFilters();
        BuildTiles();
    }

    private void OnSourceFilter(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFilters || SourceList.SelectedItem is not FilterEntry entry)
        {
            return;
        }
        _sourceFilter = entry.Source;
        BuildDateFilters();
        // The period chosen may have nothing of this provenance: show them all rather than an empty grid.
        if (DateList.SelectedItem is null && (_monthFilter is not null || _yearFilter is not null))
        {
            _monthFilter = null;
            _yearFilter = null;
            BuildSourceFilters();
        }
        BuildTagFilters();
        BuildTiles();
    }

    // ---- Grid ----

    private int ThumbnailSize => (int)SizeSlider.Value;

    private void BuildTiles()
    {
        var pixels = (int)(ThumbnailSize * VisualTreeHelper.GetDpi(this).DpiScaleX);
        _tiles = Filtered().Select(i => new PhotoTile(i, pixels)).ToList();
        _paths = _tiles.Select(t => t.Item.Path).ToList();
        _byPath = [];
        foreach (var tile in _tiles)
        {
            _byPath.TryAdd(tile.Item.Path, tile);
        }
        // Filtered out of sight, a photo leaves the selection: nothing hidden gets deleted.
        _selection.Retain(_byPath.ContainsKey);
        foreach (var path in _selection.Items)
        {
            _byPath[path].IsSelected = true;
        }
        EmptyState.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmptyState();
        var album = _albums.FirstOrDefault(a => a.Id == _albumFilter)?.Name ?? "Tous les albums";
        var period = _monthFilter is { } m ? " · " + Capitalize(m.ToString("MMMM yyyy", French)) : _yearFilter is { } y ? $" · {y}" : "";
        var source = _sourceFilter is { } kind ? $" · {MediaSource.KindLabel(kind)}" : "";
        var tags = _tagFilter.IsEmpty ? "" : $" · {_tagFilter.Describe()}";
        Caption.Text = $"{album}{source}{tags}{period} · {Formatting.Plural(_tiles.Count, "élément", "éléments")}";
        _columns = 0;
        LayoutRows();
        ShowInfo();
        UpdateCommands();
    }

    // A viewer has no phone to add: its photos come from the library.
    private void UpdateEmptyState()
    {
        if (_window.Library is not { } library)
        {
            return;
        }
        var unavailable = library.State == LibraryState.Unavailable;
        EmptyAddPhone.Visibility = Visibility.Collapsed;
        EmptyTitle.Text = unavailable ? "Bibliothèque indisponible" : "Aucune photo pour l'instant.";
        EmptyHint.Text = unavailable
            ? $"Le dossier {library.Root} ne répond pas. Mon Album PC réessaie toutes les minutes."
            : "Les photos sauvegardées sur le NAS arriveront ici automatiquement.";
    }

    /// <summary>Splits the tiles into rows of as many columns as fit, with a title per month.</summary>
    private void LayoutRows()
    {
        var width = Rows.ActualWidth - SystemParameters.VerticalScrollBarWidth - 16;
        if (width <= 0)
        {
            return;
        }
        var columns = Math.Max(1, (int)((width + Gap) / (ThumbnailSize + Gap)));
        if (columns == _columns && Rows.ItemsSource is not null)
        {
            return;
        }
        _columns = columns;
        var size = Math.Floor((width + Gap) / columns - Gap);
        var rows = new List<GridRow>();
        foreach (var month in _tiles.GroupBy(t => t.Item.Month))
        {
            rows.Add(new MonthRow(Capitalize(month.Key.ToString("MMMM yyyy", French)), Formatting.Plural(month.Count(), "élément", "éléments")));
            rows.AddRange(month.Chunk(columns).Select(chunk => new TilesRow(chunk, size)));
        }
        Rows.ItemsSource = rows;
    }

    private void OnRowsSizeChanged(object sender, SizeChangedEventArgs e) => LayoutRows();

    private void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded)
        {
            BuildTiles();
        }
    }

    private void OnTileClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not PhotoTile tile)
        {
            return;
        }
        if (e.ClickCount == 2)
        {
            _selection.Select(tile.Item.Path);
            OpenViewer();
        }
        else
        {
            var modifiers = Keyboard.Modifiers;
            _selection.Click(_paths, tile.Item.Path, modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift));
            OnSelectionChanged();
        }
        Focus();
    }

    private void Select(PhotoTile? tile)
    {
        _selection.Select(tile?.Item.Path);
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        foreach (var tile in _tiles)
        {
            tile.IsSelected = _selection.Contains(tile.Item.Path);
        }
        ShowInfo();
        UpdateCommands();
        if (Viewer.Visibility == Visibility.Visible)
        {
            ShowInViewer();
        }
    }

    private void SelectAll()
    {
        _selection.SelectAll(_paths);
        OnSelectionChanged();
    }

    private List<PhotoTile> SelectedTiles() => _selection.Count == 0 ? [] : _tiles.Where(t => _selection.Contains(t.Item.Path)).ToList();

    /// <summary>The tile last clicked if still selected, else the first selected one (after Ctrl+A).</summary>
    private PhotoTile? FocusedTile =>
        _selection.Current is { } path && _selection.Contains(path) && _byPath.TryGetValue(path, out var tile)
            ? tile
            : SelectedTiles().FirstOrDefault();

    private void UpdateCommands()
    {
        var count = _selection.Count;
        RotateLeftButton.IsEnabled = RotateRightButton.IsEnabled = count == 1 && FocusedTile is { IsVideo: false };
        DeleteButton.IsEnabled = count > 0 && !_busy;
        EmptyTrashButton.IsEnabled = _trashes.Any(t => t.Files > 0) && !_busy;
        ExplorerButton.IsEnabled = count == 1;
        FullScreenButton.IsEnabled = count > 0;
        AddTagButton.IsEnabled = count > 0;
        RemoveTagButton.IsEnabled = count > 0 && SelectedTiles().Any(t => TagsOf(t.Item).Count > 0);
    }

    // ---- Information pane ----

    private void OnInfoToggle(object sender, RoutedEventArgs e)
    {
        // Also raised while the XAML is loading, before the pane exists.
        if (InfoPane is not null)
        {
            InfoPane.Visibility = InfoToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void ShowInfo()
    {
        var selected = SelectedTiles();
        NoSelection.Visibility = selected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Details.Visibility = selected.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (selected.Count == 0)
        {
            return;
        }
        if (selected.Count > 1)
        {
            ShowSummary(selected);
            return;
        }
        var item = selected[0].Item;
        InfoName.Text = selected[0].Name;
        var taken = item.IsVideo ? null : TakenDateResolver.ReadMetadataDate(item.Path);
        var phone = _core.History.DeviceOf(item.Path) is { } id ? _core.Devices.Get(id)?.Name : null;
        var album = _albums.FirstOrDefault(a => a.Id == item.AlbumId)?.Name;
        var lines = new List<InfoLine>
        {
            new("", "Prise de vue", (taken ?? item.Date).ToString("d MMMM yyyy 'à' HH:mm", French)),
            new("", "Téléphone d'origine", phone ?? "Inconnu (copiée à la main)"),
            new("", "Provenance", SourceOf(item).Label),
            new("", "Dimensions · taille", $"{Dimensions(item)}{Size(item.Size)}"),
            new("", "Album · dossier", $"{album} · {Path.GetDirectoryName(item.Path)}{Path.DirectorySeparatorChar}"),
        };
        InfoLines.ItemsSource = lines;
        InfoTags.Show([.. TagsOf(item).Select(t => (t, 1))], _library);

        // Not imported by this PC: the journals of the album may know, but reading them can take a moment over the network.
        if (phone is null && _albums.FirstOrDefault(a => a.Id == item.AlbumId) is { } source)
        {
            var journaled = await Task.Run(() => _core.Journals.DeviceOf(source, item.Path));
            if (journaled is not null && _selection.Count == 1 && _selection.Contains(item.Path))
            {
                lines[1] = lines[1] with { Value = journaled };
                InfoLines.ItemsSource = lines.ToList();
            }
        }
    }

    private void ShowSummary(List<PhotoTile> tiles)
    {
        InfoName.Text = Formatting.Plural(tiles.Count, "élément sélectionné", "éléments sélectionnés");
        var videos = tiles.Count(t => t.IsVideo);
        var content = new List<string>();
        if (tiles.Count > videos)
        {
            content.Add(Formatting.Plural(tiles.Count - videos, "photo", "photos"));
        }
        if (videos > 0)
        {
            content.Add(Formatting.Plural(videos, "vidéo", "vidéos"));
        }
        // By month, as the grid: file dates are not always shooting dates.
        var first = tiles.Min(t => t.Item.Month).ToString("MMMM yyyy", French);
        var last = tiles.Max(t => t.Item.Month).ToString("MMMM yyyy", French);
        InfoLines.ItemsSource = new List<InfoLine>
        {
            new("", "Contenu", string.Join(" · ", content)),
            new("", "Période", first == last ? Capitalize(first) : $"De {first} à {last}"),
            new("", "Taille totale", Size(tiles.Sum(t => t.Item.Size))),
        };
        InfoTags.Show(SelectionTags(tiles), _library, tiles.Count);
    }

    /// <summary>The tags carried by <paramref name="tiles"/>, with how many carry each.</summary>
    private List<(string Tag, int Count)> SelectionTags(IEnumerable<PhotoTile> tiles) =>
    [
        .. tiles.SelectMany(t => TagsOf(t.Item))
            .GroupBy(t => t, UserTags.Same)
            .Select(g => (g.First(), g.Count()))
            .OrderBy(t => t.Item1, TagCatalog.TagOrder.Instance),
    ];

    private static string Dimensions(LibraryItem item)
    {
        if (item.IsVideo)
        {
            return "";
        }
        try
        {
            using var stream = File.OpenRead(item.Path);
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                System.Windows.Media.Imaging.BitmapCacheOption.None);
            var (w, h) = ExifOrientation.Degrees(ExifOrientation.Read(item.Path)) % 180 == 0
                ? (frame.PixelWidth, frame.PixelHeight)
                : (frame.PixelHeight, frame.PixelWidth);
            return $"{w} × {h} · ";
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException)
        {
            return "";
        }
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} Go",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} Mo",
        _ => $"{Math.Max(1, bytes / 1024)} Ko",
    };

    private sealed record InfoLine(string Icon, string Label, string Value);

    // ---- Actions ----

    private void OnRotateLeft(object sender, RoutedEventArgs e) => Rotate(clockwise: false);

    private void OnRotateRight(object sender, RoutedEventArgs e) => Rotate(clockwise: true);

    private void Rotate(bool clockwise)
    {
        if (_selection.Count != 1 || FocusedTile is not { IsVideo: false } tile)
        {
            return;
        }
        try
        {
            ExifOrientation.Rotate(tile.Item.Path, clockwise);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageMagick.MagickException)
        {
            Dialogs.Inform(_window, "Impossible de pivoter cette photo", ex.Message);
            return;
        }
        tile.Reload();
        ShowInfo();
        if (Viewer.Visibility == Visibility.Visible)
        {
            ShowInViewer();
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        var tiles = SelectedTiles();
        if (tiles.Count == 0 || _busy)
        {
            return;
        }
        // No Recycle Bin on network shares: such files go to the trash folder of their album.
        var targets = tiles.Select(t => new DeleteTarget(t.Item, t.Name,
            _albums.FirstOrDefault(a => a.Id == t.Item.AlbumId) is { } album && (_window.Library is not null || AlbumTrash.IsNetworkPath(t.Item.Path))
                ? album.DestinationRoot
                : null)).ToList();
        if (!ConfirmDelete(targets))
        {
            return;
        }
        // By path: a refresh of the library may have rebuilt the tiles while the dialog was open.
        var order = _tiles;
        var index = Math.Max(0, order.FindIndex(t => t.Item.Path == targets[0].Item.Path));
        ViewerVideo.Stop();
        ViewerVideo.Source = null;
        ViewerImage.Source = null;
        _busy = true;
        UpdateCommands();
        Mouse.OverrideCursor = Cursors.Wait;
        List<DeleteFailure> failures;
        try
        {
            failures = await RunOnStaThread(() => Delete(targets));
        }
        finally
        {
            _busy = false;
            Mouse.OverrideCursor = null;
        }
        var failed = failures.Select(f => f.Target.Item.Path).ToHashSet();
        var deleted = targets.Select(t => t.Item.Path).Where(p => !failed.Contains(p)).ToHashSet();
        _all.RemoveAll(i => deleted.Contains(i.Path));
        var remaining = order.Where(t => !deleted.Contains(t.Item.Path)).ToList();
        var next = remaining.Count == 0 ? null : remaining[Math.Min(index, remaining.Count - 1)];
        _selection.Select(null);
        BuildFilters();
        BuildTiles();
        Select(next is null ? null : _byPath.GetValueOrDefault(next.Item.Path));
        if (FocusedTile is null)
        {
            CloseViewer();
        }
        UpdateTrash();
        ReportFailures(failures, targets.Count);
    }

    private bool ConfirmDelete(List<DeleteTarget> targets)
    {
        var toAlbums = targets.Count(t => t.AlbumRoot is not null);
        var trashes = targets.Select(t => t.AlbumRoot).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var trash = trashes.Count == 1 ? AlbumTrash.Folder(trashes[0]) : null;
        if (targets is [var single])
        {
            var title = single.Item.IsVideo ? "Supprimer cette vidéo ?" : "Supprimer cette photo ?";
            return single.AlbumRoot is not null
                ? Dialogs.Confirm(_window, title, $"{single.Name} sera déplacé dans la corbeille de l'album. Vous pourrez l'y récupérer.",
                    "Supprimer", trash, destructive: true)
                : Dialogs.Confirm(_window, title, $"{single.Name} sera placé dans la Corbeille de Windows. Vous pourrez le restaurer depuis la Corbeille.",
                    "Supprimer", destructive: true);
        }
        var message = toAlbums == 0
            ? "Ils seront placés dans la Corbeille de Windows. Vous pourrez les restaurer depuis la Corbeille."
            : toAlbums == targets.Count
                ? "Ils seront déplacés dans la corbeille de leur album. Vous pourrez les y récupérer."
                : $"{Formatting.Plural(toAlbums, "élément ira", "éléments iront")} dans la corbeille de leur album, les autres dans la " +
                  "Corbeille de Windows. Vous pourrez les récupérer.";
        return Dialogs.Confirm(_window, $"Supprimer {Formatting.Plural(targets.Count, "élément", "éléments")} ?", message, "Supprimer",
            trash, destructive: true);
    }

    // One file at a time, so that a file that resists does not keep the others.
    private static List<DeleteFailure> Delete(List<DeleteTarget> targets)
    {
        var failures = new List<DeleteFailure>();
        foreach (var target in targets)
        {
            try
            {
                if (target.AlbumRoot is { } root)
                {
                    AlbumTrash.Move(root, target.Item.Path);
                }
                else
                {
                    FileSystem.DeleteFile(target.Item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                failures.Add(new DeleteFailure(target, ex.Message));
            }
        }
        return failures;
    }

    private void ReportFailures(List<DeleteFailure> failures, int total)
    {
        if (failures.Count == 0)
        {
            return;
        }
        if (total == 1)
        {
            Dialogs.Inform(_window, $"Impossible de supprimer {failures[0].Target.Name}", failures[0].Error);
            return;
        }
        var lines = failures.Take(5).Select(f => $"{f.Target.Name} : {f.Error}").ToList();
        if (failures.Count > lines.Count)
        {
            lines.Add($"… et {Formatting.Plural(failures.Count - lines.Count, "autre", "autres")}");
        }
        Dialogs.Inform(_window, "Suppression incomplète",
            $"{Formatting.Plural(failures.Count, "élément n'a pas pu être supprimé", "éléments n'ont pas pu être supprimés")} sur {Formatting.Count(total)}.",
            string.Join("\n", lines));
    }

    // Many files on a share take a while: off the UI thread, on an STA one as the Recycle Bin is a shell operation.
    private static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    /// <summary>A file to delete; <see cref="AlbumRoot"/> is the album whose trash receives it, null for the Recycle Bin.</summary>
    private sealed record DeleteTarget(LibraryItem Item, string Name, string? AlbumRoot);

    private sealed record DeleteFailure(DeleteTarget Target, string Error);

    // ---- Tags ----

    /// <summary>The tags of the index by full path (a query: no file is opened), and the tag list of the library.</summary>
    private (Dictionary<string, IReadOnlyList<string>>, IReadOnlyList<TagCount>) LoadTags(IReadOnlyList<Album> albums)
    {
        var roots = albums.ToDictionary(a => a.Id, a => a.DestinationRoot);
        var byPath = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var ((albumId, relative), tags) in _core.Tags.ByFile())
        {
            if (roots.TryGetValue(albumId, out var root))
            {
                byPath[Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))] = tags;
            }
        }
        return (byPath, _core.Tags.List());
    }

    /// <summary>Takes the tags from the index again; the grid is rebuilt only when the tag filter is in use.</summary>
    private async void ReloadTags()
    {
        var albums = _albums;
        try
        {
            (_tags, _library) = await Task.Run(() => LoadTags(albums));
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return; // the catalog is busy: the next change brings them
        }
        AfterTagsChanged();
    }

    private void AfterTagsChanged()
    {
        if (_tagFilter.IsEmpty)
        {
            BuildTagFilters();
            ShowInfo();
            UpdateCommands();
            if (Viewer.Visibility == Visibility.Visible && FocusedTile is { } tile)
            {
                ViewerTags.Show([.. TagsOf(tile.Item).Select(t => (t, 1))], _library);
            }
        }
        else
        {
            OnTagFilterChanged();
        }
    }

    private void OnAddTagClick(object sender, RoutedEventArgs e)
    {
        var count = _selection.Count;
        AddTagCaption.Text = $"Ajouter un tag à {Formatting.Plural(count, "élément", "éléments")}";
        AddTagBox.Tags = _library;
        AddTagBox.Exclude = SelectionTags(SelectedTiles()).Where(t => t.Count == count).Select(t => t.Tag).ToList();
        AddTagPopup.IsOpen = true;
        AddTagBox.Start();
    }

    private void OnRemoveTagClick(object sender, RoutedEventArgs e)
    {
        var count = _selection.Count;
        RemoveTagList.ItemsSource = SelectionTags(SelectedTiles()).Select(t => new SelectionTag(t.Tag, $"{Formatting.Count(t.Count)} sur {Formatting.Count(count)}")).ToList();
        RemoveTagPopup.IsOpen = true;
    }

    private void OnRemoveTagChosen(object sender, RoutedEventArgs e)
    {
        RemoveTagPopup.IsOpen = false;
        if (sender is FrameworkElement { Tag: string tag })
        {
            ApplyTag(SelectedTiles().Select(t => t.Item).ToList(), tag, add: false);
        }
    }

    /// <summary>
    /// Adds or removes a tag: shown at once, while the files are rewritten in the background (the activity board and the
    /// status card tell how far it got). Only failures are told at the end.
    /// </summary>
    private async void ApplyTag(IReadOnlyList<LibraryItem> items, string tag, bool add)
    {
        var name = add ? _core.Tags.Canonical(tag) ?? tag : tag;
        var albums = _albums.ToDictionary(a => a.Id);
        var targets = items.Where(i => TagsOf(i).Contains(name, UserTags.Same) != add && albums.ContainsKey(i.AlbumId)).ToList();
        if (targets.Count == 0)
        {
            return;
        }
        foreach (var item in targets)
        {
            _tags[item.Path] = add ? [.. TagsOf(item), name] : [.. TagsOf(item).Where(t => !UserTags.Same.Equals(t, name))];
        }
        if (add && !_library.Any(t => UserTags.Same.Equals(t.Name, name)))
        {
            _library = [.. _library, new TagCount(name, targets.Count)];
        }
        AfterTagsChanged();
        var files = targets.Select(i => new TagTarget(albums[i.AlbumId], i.Path)).ToList();
        TagRunResult result;
        try
        {
            result = await (add ? _core.Tags.AddAsync(files, name) : _core.Tags.RemoveAsync(files, name));
        }
        catch (Exception e)
        {
            ReloadTags();
            Dialogs.Inform(_window, add ? $"Impossible d'ajouter le tag « {name} »" : $"Impossible de retirer le tag « {name} »", e.Message);
            return;
        }
        ReloadTags();
        if (result.Failures.Count > 0)
        {
            var lines = result.Failures.Take(5).Select(f => $"{f.Name} : {f.Reason}").ToList();
            if (result.Failures.Count > lines.Count)
            {
                lines.Add($"… et {Formatting.Plural(result.Failures.Count - lines.Count, "autre", "autres")}");
            }
            Dialogs.Inform(_window, add ? "Tag ajouté en partie" : "Tag retiré en partie", result.Summary + ".", string.Join("\n", lines));
        }
    }

    // ---- Album trash ----

    /// <summary>Measures the trash of the albums in view, on the network for most of them: off the UI thread.</summary>
    private async void UpdateTrash()
    {
        var measure = ++_trashMeasure;
        var albums = _albums.Where(a => _albumFilter is null || a.Id == _albumFilter).ToList();
        var viewer = _window.Library is not null;
        var trashes = await Task.Run(() => albums
            // Local albums of an autonomous PC use the Windows Recycle Bin, which is not ours to empty.
            .Where(a => viewer || AlbumTrash.IsNetworkPath(a.DestinationRoot))
            .Select(MeasureTrash)
            .ToList());
        if (measure != _trashMeasure)
        {
            return; // a newer measure is on its way
        }
        _trashes = trashes;
        var files = trashes.Sum(t => t.Files);
        EmptyTrashButton.Visibility = trashes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TrashCount.Text = files == 0 ? "" : Formatting.Count(files);
        EmptyTrashButton.ToolTip = files == 0
            ? "La corbeille est vide"
            : $"{Formatting.Plural(files, "élément", "éléments")} · {Size(trashes.Sum(t => t.Bytes))}";
        UpdateCommands();
    }

    private static TrashContent MeasureTrash(Album album)
    {
        try
        {
            var (files, bytes) = AlbumTrash.Measure(album.DestinationRoot);
            return new TrashContent(album, files, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new TrashContent(album, 0, 0); // the share does not answer: nothing to empty for now
        }
    }

    private async void OnEmptyTrash(object sender, RoutedEventArgs e)
    {
        var full = _trashes.Where(t => t.Files > 0).ToList();
        if (full.Count == 0 || _busy)
        {
            return;
        }
        var files = full.Sum(t => t.Files);
        var where = full is [var single] ? $"de la corbeille de l'album {single.Album.Name}" : $"des corbeilles de {full.Count} albums";
        if (!Dialogs.Confirm(_window, "Vider la corbeille ?",
                $"{Formatting.Plural(files, "élément", "éléments")} ({Size(full.Sum(t => t.Bytes))}) {where} " +
                $"{(files == 1 ? "sera supprimé" : "seront supprimés")}. Cette action est définitive.",
                "Vider la corbeille", destructive: true))
        {
            return;
        }
        _busy = true;
        UpdateCommands();
        Mouse.OverrideCursor = Cursors.Wait;
        List<string> failures;
        try
        {
            failures = await Task.Run(() => full.SelectMany(EmptyTrash).ToList());
        }
        finally
        {
            _busy = false;
            Mouse.OverrideCursor = null;
        }
        UpdateTrash();
        if (failures.Count > 0)
        {
            Dialogs.Inform(_window, "Impossible de vider la corbeille", "Des fichiers n'ont pas pu être supprimés.", string.Join("\n", failures));
        }
    }

    private static IEnumerable<string> EmptyTrash(TrashContent trash)
    {
        try
        {
            AlbumTrash.Empty(trash.Album.DestinationRoot);
            return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [$"{trash.Album.Name} : {e.Message}"];
        }
    }

    private sealed record TrashContent(Album Album, int Files, long Bytes);

    private void OnOpenInExplorer(object sender, RoutedEventArgs e)
    {
        if (_selection.Count == 1 && FocusedTile is { } tile)
        {
            Process.Start("explorer.exe", $"/select,\"{tile.Item.Path}\"");
        }
    }

    private void OnAddPhone(object sender, RoutedEventArgs e) => _window.ShowAddPhone(null);

    // ---- Full screen ----

    private void OnFullScreen(object sender, RoutedEventArgs e) => OpenViewer();

    private void OpenViewer()
    {
        if (FocusedTile is not { } tile)
        {
            return;
        }
        Viewer.Visibility = Visibility.Visible;
        // One photo at a time in full screen: the commands there act on the one shown.
        Select(tile);
        Focus();
    }

    private void ShowInViewer()
    {
        if (FocusedTile is not { } tile)
        {
            return;
        }
        var index = _tiles.IndexOf(tile);
        ViewerName.Text = tile.Name;
        var taken = tile.IsVideo ? null : TakenDateResolver.ReadMetadataDate(tile.Item.Path);
        ViewerPosition.Text = $"{(taken ?? tile.Item.Date).ToString("d MMMM yyyy", French)} · {index + 1} sur {_tiles.Count}";
        ViewerTags.Show([.. TagsOf(tile.Item).Select(t => (t, 1))], _library);
        PreviousButton.Opacity = index > 0 ? 1 : 0.3;
        NextButton.Opacity = index < _tiles.Count - 1 ? 1 : 0.3;

        ViewerVideo.Stop();
        if (tile.IsVideo)
        {
            ViewerImage.Source = tile.Thumbnail;
            ViewerVideo.Source = new Uri(tile.Item.Path);
            ViewerVideo.Visibility = Visibility.Collapsed;
            PlayButton.Visibility = Visibility.Visible;
        }
        else
        {
            ViewerVideo.Source = null;
            ViewerVideo.Visibility = Visibility.Collapsed;
            PlayButton.Visibility = Visibility.Collapsed;
            var pixels = (int)(Math.Max(ActualWidth, ActualHeight) * VisualTreeHelper.GetDpi(this).DpiScaleX);
            ViewerImage.Source = Thumbnails.LoadFull(tile.Item.Path, Math.Max(pixels, 1024));
        }
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        PlayButton.Visibility = Visibility.Collapsed;
        ViewerImage.Source = null;
        ViewerVideo.Visibility = Visibility.Visible;
        ViewerVideo.Play();
    }

    private void OnVideoEnded(object sender, RoutedEventArgs e)
    {
        ViewerVideo.Stop();
        PlayButton.Visibility = Visibility.Visible;
    }

    private void OnPrevious(object sender, RoutedEventArgs e) => Move(-1);

    private void OnNext(object sender, RoutedEventArgs e) => Move(1);

    /// <summary>Selects the previous or next tile; <paramref name="extend"/> (Shift) extends the selection to it instead.</summary>
    private void Move(int delta, bool extend = false)
    {
        if (FocusedTile is not { } current)
        {
            return;
        }
        var index = _tiles.IndexOf(current) + delta;
        if (index < 0 || index >= _tiles.Count)
        {
            return;
        }
        if (extend)
        {
            _selection.Click(_paths, _paths[index], toggle: false, range: true);
            OnSelectionChanged();
        }
        else
        {
            Select(_tiles[index]);
        }
    }

    private void OnCloseViewer(object sender, RoutedEventArgs e) => CloseViewer();

    private void CloseViewer()
    {
        ViewerVideo.Stop();
        ViewerVideo.Source = null;
        ViewerImage.Source = null;
        Viewer.Visibility = Visibility.Collapsed;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }
        var inViewer = Viewer.Visibility == Visibility.Visible;
        var extend = !inViewer && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Left: Move(-1, extend); e.Handled = true; break;
            case Key.Right: Move(1, extend); e.Handled = true; break;
            case Key.A when !inViewer && Keyboard.Modifiers == ModifierKeys.Control: SelectAll(); e.Handled = true; break;
            case Key.Enter when !inViewer: OpenViewer(); e.Handled = true; break;
            case Key.Escape when inViewer: CloseViewer(); e.Handled = true; break;
            case Key.Delete: OnDelete(this, e); e.Handled = true; break;
        }
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], French) + text[1..];
}
