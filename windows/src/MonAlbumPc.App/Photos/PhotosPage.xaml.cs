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
    private List<PhotoTile> _tiles = [];
    private List<string> _paths = [];
    private Dictionary<string, PhotoTile> _byPath = [];
    // By path, so that the selection outlives the tiles rebuilt by a refresh.
    private readonly Selection<string> _selection = new();
    private bool _deleting;
    private Guid? _albumFilter;
    private DateTime? _monthFilter;
    private int? _yearFilter;
    private int _columns;
    private bool _updatingFilters;

    public PhotosPage(CoreServices core, MainWindow window)
    {
        _core = core;
        _window = window;
        InitializeComponent();
        UpdateCommands();
    }

    public async void Refresh()
    {
        if (_window.Library is { State: LibraryState.Unavailable } && _all.Count > 0)
        {
            return; // the share does not answer: what is on screen is better than an empty page
        }
        _albums = _core.Albums.List();
        var albums = _albums;
        // Walking big album folders takes a while: keep the window responsive.
        _all = await Task.Run(() => albums.SelectMany(PhotoLibrary.Scan).ToList());
        BuildFilters();
        BuildTiles();
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

        var inAlbum = InAlbum().ToList();
        var dates = new List<FilterEntry>();
        foreach (var year in inAlbum.GroupBy(i => i.Month.Year).OrderByDescending(g => g.Key))
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

    private IEnumerable<LibraryItem> Filtered() => InAlbum()
        .Where(i => _monthFilter is null || i.Month == _monthFilter)
        .Where(i => _yearFilter is null || i.Month.Year == _yearFilter);

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
        Caption.Text = $"{album}{period} · {Formatting.Plural(_tiles.Count, "élément", "éléments")}";
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
        DeleteButton.IsEnabled = count > 0 && !_deleting;
        ExplorerButton.IsEnabled = count == 1;
        FullScreenButton.IsEnabled = count > 0;
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
        if (selected.Count != 1)
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
            new("", "Dimensions · taille", $"{Dimensions(item)}{Size(item.Size)}"),
            new("", "Album · dossier", $"{album} · {Path.GetDirectoryName(item.Path)}{Path.DirectorySeparatorChar}"),
        };
        InfoLines.ItemsSource = lines;

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
        if (tiles.Count == 0)
        {
            return;
        }
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
    }

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
        if (tiles.Count == 0 || _deleting)
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
        var order = _tiles;
        var index = order.IndexOf(tiles[0]);
        ViewerVideo.Stop();
        ViewerVideo.Source = null;
        ViewerImage.Source = null;
        _deleting = true;
        UpdateCommands();
        Mouse.OverrideCursor = Cursors.Wait;
        List<DeleteFailure> failures;
        try
        {
            failures = await RunOnStaThread(() => Delete(targets));
        }
        finally
        {
            _deleting = false;
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
