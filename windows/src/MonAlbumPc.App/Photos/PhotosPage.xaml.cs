using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;
using MonAlbumPc.Core;
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
    private PhotoTile? _selected;
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
        var selectedPath = _selected?.Item.Path;
        _tiles = Filtered().Select(i => new PhotoTile(i, pixels)).ToList();
        _selected = _tiles.FirstOrDefault(t => t.Item.Path == selectedPath);
        if (_selected is not null)
        {
            _selected.IsSelected = true;
        }
        EmptyState.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var album = _albums.FirstOrDefault(a => a.Id == _albumFilter)?.Name ?? "Tous les albums";
        var period = _monthFilter is { } m ? " · " + Capitalize(m.ToString("MMMM yyyy", French)) : _yearFilter is { } y ? $" · {y}" : "";
        Caption.Text = $"{album}{period} · {Formatting.Plural(_tiles.Count, "élément", "éléments")}";
        _columns = 0;
        LayoutRows();
        ShowInfo();
        UpdateCommands();
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
        Select(tile);
        if (e.ClickCount == 2)
        {
            OpenViewer();
        }
        Focus();
    }

    private void Select(PhotoTile? tile)
    {
        if (_selected is not null)
        {
            _selected.IsSelected = false;
        }
        _selected = tile;
        if (tile is not null)
        {
            tile.IsSelected = true;
        }
        ShowInfo();
        UpdateCommands();
        if (Viewer.Visibility == Visibility.Visible)
        {
            ShowInViewer();
        }
    }

    private void UpdateCommands()
    {
        var hasSelection = _selected is not null;
        var isPhoto = hasSelection && !_selected!.IsVideo;
        RotateLeftButton.IsEnabled = RotateRightButton.IsEnabled = isPhoto;
        DeleteButton.IsEnabled = ExplorerButton.IsEnabled = FullScreenButton.IsEnabled = hasSelection;
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

    private void ShowInfo()
    {
        if (_selected is null)
        {
            NoSelection.Visibility = Visibility.Visible;
            Details.Visibility = Visibility.Collapsed;
            return;
        }
        NoSelection.Visibility = Visibility.Collapsed;
        Details.Visibility = Visibility.Visible;
        var item = _selected.Item;
        InfoName.Text = _selected.Name;
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
        if (_selected is not { IsVideo: false } tile)
        {
            return;
        }
        try
        {
            ExifOrientation.Rotate(tile.Item.Path, clockwise);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageMagick.MagickException)
        {
            MessageBox.Show(_window, $"Impossible de pivoter cette photo.\n\n{ex.Message}", "Pivoter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        tile.Reload();
        ShowInfo();
        if (Viewer.Visibility == Visibility.Visible)
        {
            ShowInViewer();
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } tile)
        {
            return;
        }
        var what = tile.IsVideo ? "cette vidéo" : "cette photo";
        var answer = MessageBox.Show(_window,
            $"{tile.Name} sera placé dans la Corbeille de Windows. Vous pourrez le restaurer depuis la Corbeille.",
            $"Supprimer {what} ?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        var index = _tiles.IndexOf(tile);
        ViewerVideo.Stop();
        ViewerVideo.Source = null;
        ViewerImage.Source = null;
        try
        {
            FileSystem.DeleteFile(tile.Item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return;
        }
        _all.RemoveAll(i => i.Path == tile.Item.Path);
        _tiles.Remove(tile);
        var next = _tiles.Count == 0 ? null : _tiles[Math.Min(index, _tiles.Count - 1)];
        _selected = null;
        BuildFilters();
        BuildTiles();
        Select(next is null ? null : _tiles.FirstOrDefault(t => t.Item.Path == next.Item.Path));
        if (_selected is null)
        {
            CloseViewer();
        }
    }

    private void OnOpenInExplorer(object sender, RoutedEventArgs e)
    {
        if (_selected is { } tile)
        {
            Process.Start("explorer.exe", $"/select,\"{tile.Item.Path}\"");
        }
    }

    private void OnAddPhone(object sender, RoutedEventArgs e) => _window.ShowAddPhone(null);

    // ---- Full screen ----

    private void OnFullScreen(object sender, RoutedEventArgs e) => OpenViewer();

    private void OpenViewer()
    {
        if (_selected is null)
        {
            return;
        }
        Viewer.Visibility = Visibility.Visible;
        ShowInViewer();
        Focus();
    }

    private void ShowInViewer()
    {
        if (_selected is not { } tile)
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

    private void Move(int delta)
    {
        if (_selected is null)
        {
            return;
        }
        var index = _tiles.IndexOf(_selected) + delta;
        if (index >= 0 && index < _tiles.Count)
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
        switch (e.Key)
        {
            case Key.Left: Move(-1); e.Handled = true; break;
            case Key.Right: Move(1); e.Handled = true; break;
            case Key.Enter when Viewer.Visibility != Visibility.Visible: OpenViewer(); e.Handled = true; break;
            case Key.Escape when Viewer.Visibility == Visibility.Visible: CloseViewer(); e.Handled = true; break;
            case Key.Delete: OnDelete(this, e); e.Handled = true; break;
        }
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], French) + text[1..];
}
