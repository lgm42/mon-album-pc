using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using MonAlbumPc.App.Photos;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.App.Map;

/// <summary>
/// Map of the located photos (Leaflet in WebView2, OpenStreetMap tiles) and, on the right, the photos of the visible
/// area, newest first, grouped by month.
/// </summary>
public partial class MapPage : UserControl, IRefreshable
{
    private const string Host = "monalbumpc.example";
    private const string UserAgent = "MonAlbumPC/0.1 (+https://github.com/lgm42/mon-album-pc)";
    private const string RuntimeDownload = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private const double Gap = 4;
    private const double ThumbnailSize = 112;
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly CoreServices _core;
    private List<Album> _albums = [];
    private Guid? _albumFilter;
    private bool _updatingFilter;

    /// <summary>Located media of the current filter; the index is the id given to the map.</summary>
    private List<LocatedMedia> _points = [];
    private readonly Dictionary<int, PhotoTile> _tileCache = [];
    private readonly Dictionary<PhotoTile, int> _ids = [];
    private List<PhotoTile> _tiles = [];
    private PhotoTile? _selected;
    private Bounds? _bounds;
    private int _columns;

    private Task? _mapInit;
    private bool _mapReady;
    private bool _pointsSent;
    private bool _fitPending = true;
    private int _sentSignature;

    public MapPage(CoreServices core)
    {
        _core = core;
        InitializeComponent();
        _core.MediaIndexer.Progress += OnIndexProgress;
        _core.MediaIndexer.Completed += OnIndexCompleted;
        Loaded += (_, _) => _mapInit ??= InitializeMapAsync();
    }

    // ---- Data ----

    public async void Refresh()
    {
        _albums = _core.Albums.List();
        BuildAlbumFilter();
        UpdateIndexing(null);
        var albumId = _albumFilter;
        var points = await Task.Run(() => _core.Media.ListLocated(albumId));
        if (albumId != _albumFilter)
        {
            return; // the filter changed meanwhile: a newer load is on its way
        }
        var signature = Signature(points, albumId);
        if (_pointsSent && signature == _sentSignature)
        {
            return; // nothing new: keep the map and the list as they are
        }
        if (_points.Count == 0)
        {
            _fitPending = true;
        }
        _points = points;
        _tileCache.Clear();
        _ids.Clear();
        _sentSignature = signature;
        _pointsSent = false;
        SendPoints();
        BuildTiles();
    }

    private static int Signature(List<LocatedMedia> points, Guid? albumId)
    {
        var hash = new HashCode();
        hash.Add(albumId);
        foreach (var point in points)
        {
            hash.Add(point.Path);
            hash.Add(point.Latitude);
            hash.Add(point.Longitude);
            hash.Add(point.TakenAt);
        }
        return hash.ToHashCode();
    }

    private void BuildAlbumFilter()
    {
        _updatingFilter = true;
        var choices = new List<AlbumChoice> { new("Tous les albums", null, null) };
        choices.AddRange(_albums.Select(a => new AlbumChoice(a.Name, a.Id, Brush(a.Color))));
        AlbumFilter.ItemsSource = choices;
        AlbumFilter.SelectedItem = choices.FirstOrDefault(c => c.AlbumId == _albumFilter) ?? choices[0];
        _albumFilter = ((AlbumChoice)AlbumFilter.SelectedItem).AlbumId;
        _updatingFilter = false;
    }

    private void OnAlbumFilter(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFilter || AlbumFilter.SelectedItem is not AlbumChoice choice)
        {
            return;
        }
        _albumFilter = choice.AlbumId;
        _fitPending = true;
        Refresh();
    }

    private void OnIndexProgress(MediaIndexProgress progress) => Dispatcher.BeginInvoke(() => UpdateIndexing(progress));

    private void OnIndexCompleted(bool changed) => Dispatcher.BeginInvoke(() =>
    {
        UpdateIndexing(null);
        if (changed && IsVisible)
        {
            Refresh();
        }
    });

    private void UpdateIndexing(MediaIndexProgress? progress)
    {
        if (!_core.MediaIndexer.IsRunning)
        {
            Indexing.Visibility = Visibility.Collapsed;
            return;
        }
        Indexing.Visibility = Visibility.Visible;
        if (progress is { Total: > 0 } p)
        {
            IndexingText.Text = $"Recherche des lieux de prise de vue dans {p.AlbumName} : {Formatting.Count(p.Done)} sur {Formatting.Count(p.Total)}.";
            IndexingProgress.IsIndeterminate = false;
            IndexingProgress.Maximum = p.Total;
            IndexingProgress.Value = p.Done;
        }
        else
        {
            IndexingText.Text = "Recherche des lieux de prise de vue dans les albums…";
            IndexingProgress.IsIndeterminate = true;
        }
    }

    // ---- Map ----

    private async Task InitializeMapAsync()
    {
        MapUnavailable.Visibility = Visibility.Collapsed;
        MapLoading.Visibility = Visibility.Visible;
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonAlbumPC", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await MapView.EnsureCoreWebView2Async(environment);
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or COMException or IOException or UnauthorizedAccessException
                                      or InvalidOperationException or ArgumentException)
        {
            ShowMapUnavailable(e is WebView2RuntimeNotFoundException
                ? "Elle a besoin du composant Microsoft Edge WebView2, qui n'est pas installé sur ce PC."
                : $"Le composant Microsoft Edge WebView2 n'a pas pu démarrer. Réinstallez-le, puis réessayez.\n({e.Message})");
            return;
        }

        var web = MapView.CoreWebView2;
        web.Settings.UserAgent = UserAgent; // OpenStreetMap tile policy: identify the app
        web.Settings.AreDefaultContextMenusEnabled = false;
        web.Settings.IsStatusBarEnabled = false;
        web.Settings.IsZoomControlEnabled = false;
        web.Settings.AreBrowserAcceleratorKeysEnabled = false;
        web.Settings.IsGeneralAutofillEnabled = false;
#if !DEBUG
        web.Settings.AreDevToolsEnabled = false;
#endif
        web.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "Map", "web"),
            CoreWebView2HostResourceAccessKind.DenyCors);
        web.WebMessageReceived += OnWebMessage;
        web.ProcessFailed += (_, _) => ShowMapUnavailable("La carte s'est arrêtée de manière inattendue. Réessayez.");
        // Links (OpenStreetMap copyright, Leaflet) open in the browser, never inside the app.
        web.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                OpenInBrowser(e.Uri);
            }
        };
        web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenInBrowser(e.Uri);
        };
        web.Navigate($"https://{Host}/map.html");
    }

    private void ShowMapUnavailable(string text)
    {
        _mapReady = false;
        _pointsSent = false;
        _mapInit = null;
        MapView.Visibility = Visibility.Hidden;
        MapLoading.Visibility = Visibility.Collapsed;
        MapUnavailableText.Text = text;
        MapUnavailable.Visibility = Visibility.Visible;
    }

    private void OnInstallWebView2(object sender, RoutedEventArgs e) => OpenInBrowser(RuntimeDownload);

    private void OnRetryMap(object sender, RoutedEventArgs e) => _mapInit = InitializeMapAsync();

    private static void OpenInBrowser(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http")
        {
            Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var json = JsonDocument.Parse(e.WebMessageAsJson);
        var message = json.RootElement;
        switch (message.GetProperty("type").GetString())
        {
            case "ready":
                _mapReady = true;
                MapLoading.Visibility = Visibility.Collapsed;
                MapView.Visibility = Viewer.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
                _fitPending = true;
                _pointsSent = false;
                SendPoints();
                break;
            case "bounds":
                _bounds = new Bounds(message.GetProperty("south").GetDouble(), message.GetProperty("west").GetDouble(),
                    message.GetProperty("north").GetDouble(), message.GetProperty("east").GetDouble());
                BuildTiles();
                break;
            case "select":
                var id = message.GetProperty("id").GetInt32();
                if (_tiles.FirstOrDefault(t => _ids[t] == id) is { } tile)
                {
                    Select(tile);
                    ScrollTo(tile);
                }
                break;
        }
    }

    /// <summary>Sends every point at once, as compact arrays: [id, lat, lon, colorIndex, name, date].</summary>
    private void SendPoints()
    {
        if (!_mapReady || _pointsSent)
        {
            return;
        }
        var colorIndex = _albums.Select((a, i) => (a.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "points");
            writer.WriteBoolean("fit", _fitPending);
            writer.WriteStartArray("colors");
            foreach (var album in _albums)
            {
                writer.WriteStringValue(album.Color);
            }
            writer.WriteEndArray();
            writer.WriteStartArray("points");
            for (var i = 0; i < _points.Count; i++)
            {
                var point = _points[i];
                writer.WriteStartArray();
                writer.WriteNumberValue(i);
                writer.WriteNumberValue(Math.Round(point.Latitude, 6));
                writer.WriteNumberValue(Math.Round(point.Longitude, 6));
                writer.WriteNumberValue(colorIndex.GetValueOrDefault(point.AlbumId));
                writer.WriteStringValue(Path.GetFileName(point.Path));
                writer.WriteStringValue(point.TakenAt.ToString("d MMMM yyyy 'à' HH:mm", French));
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        MapView.CoreWebView2.PostWebMessageAsJson(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        _pointsSent = true;
        _fitPending = false;
    }

    private void Highlight(int? id)
    {
        if (_mapReady)
        {
            MapView.CoreWebView2.PostWebMessageAsJson(id is { } value ? $$"""{"type":"highlight","id":{{value}}}""" : """{"type":"highlight","id":null}""");
        }
    }

    // ---- Photos of the visible area ----

    private void BuildTiles()
    {
        var pixels = (int)(ThumbnailSize * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var visible = new List<PhotoTile>();
        // Without bounds from the map yet (or no map at all), the whole list is shown.
        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            if (_bounds is { } bounds && !bounds.Contains(point.Latitude, point.Longitude))
            {
                continue;
            }
            if (!_tileCache.TryGetValue(i, out var tile))
            {
                var month = new DateTime(point.TakenAt.Year, point.TakenAt.Month, 1);
                tile = new PhotoTile(new LibraryItem(point.Path, point.AlbumId, month, point.TakenAt, point.IsVideo, point.Size), pixels);
                _tileCache[i] = tile;
                _ids[tile] = i;
            }
            visible.Add(tile);
        }
        _tiles = visible;
        if (_selected is not null && !_ids.ContainsKey(_selected))
        {
            _selected = null; // points reloaded
        }

        Caption.Text = _points.Count == 0
            ? "Aucune photo localisée"
            : $"{Formatting.Plural(_tiles.Count, "photo", "photos")} dans cette zone";
        EmptyArea.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyAreaText.Text = _points.Count == 0
            ? "Aucune photo localisée pour l'instant. Les photos prises avec la localisation activée apparaîtront ici."
            : "Aucune photo dans cette zone. Déplacez la carte ou dézoomez pour en voir d'autres.";
        _columns = 0;
        LayoutRows();
        ShowDetails();
    }

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

    private void ScrollTo(PhotoTile tile)
    {
        if (Rows.ItemsSource is IEnumerable<GridRow> rows
            && rows.OfType<TilesRow>().FirstOrDefault(r => r.Tiles.Contains(tile)) is { } row)
        {
            Rows.ScrollIntoView(row);
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
        // A click on a marker already shows where the photo is; the ring makes it stand out among its neighbours too.
        Highlight(tile is null ? null : _ids[tile]);
        ShowDetails();
        if (Viewer.Visibility == Visibility.Visible)
        {
            ShowInViewer();
        }
    }

    private void ShowDetails()
    {
        if (_selected is null || !_ids.TryGetValue(_selected, out var id))
        {
            Details.Visibility = Visibility.Collapsed;
            return;
        }
        var point = _points[id];
        Details.Visibility = Visibility.Visible;
        DetailsName.Text = _selected.Name;
        var album = _albums.FirstOrDefault(a => a.Id == point.AlbumId)?.Name;
        DetailsDate.Text = $"{point.TakenAt.ToString("d MMMM yyyy 'à' HH:mm", French)}{(album is null ? "" : " · " + album)}";
        DetailsPlace.Text = Coordinates(point.Latitude, point.Longitude);
    }

    private static string Coordinates(double latitude, double longitude) =>
        string.Create(French, $"{Math.Abs(latitude):0.0000}° {(latitude < 0 ? "S" : "N")}, {Math.Abs(longitude):0.0000}° {(longitude < 0 ? "O" : "E")}");

    private void OnOpenInExplorer(object sender, RoutedEventArgs e)
    {
        if (_selected is { } tile)
        {
            Process.Start("explorer.exe", $"/select,\"{tile.Item.Path}\"");
        }
    }

    // ---- Full screen ----

    private void OnFullScreen(object sender, RoutedEventArgs e) => OpenViewer();

    private void OpenViewer()
    {
        if (_selected is null)
        {
            return;
        }
        MapView.Visibility = Visibility.Hidden;
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
        ViewerPosition.Text = $"{tile.Item.Date.ToString("d MMMM yyyy", French)} · {index + 1} sur {_tiles.Count}";
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
            ScrollTo(_tiles[index]);
        }
    }

    private void OnCloseViewer(object sender, RoutedEventArgs e) => CloseViewer();

    private void CloseViewer()
    {
        ViewerVideo.Stop();
        ViewerVideo.Source = null;
        ViewerImage.Source = null;
        Viewer.Visibility = Visibility.Collapsed;
        if (_mapReady)
        {
            MapView.Visibility = Visibility.Visible;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Move(-1); e.Handled = true; break;
            case Key.Right: Move(1); e.Handled = true; break;
            case Key.Enter when Viewer.Visibility != Visibility.Visible: OpenViewer(); e.Handled = true; break;
            case Key.Escape when Viewer.Visibility == Visibility.Visible: CloseViewer(); e.Handled = true; break;
        }
    }

    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], French) + text[1..];

    /// <summary>Visible area of the map. Longitudes may go past ±180° when the map wraps around the world.</summary>
    private readonly record struct Bounds(double South, double West, double North, double East)
    {
        public bool Contains(double latitude, double longitude)
        {
            if (latitude < South || latitude > North)
            {
                return false;
            }
            if (East - West >= 360)
            {
                return true;
            }
            var x = longitude;
            while (x < West)
            {
                x += 360;
            }
            while (x - 360 >= West)
            {
                x -= 360;
            }
            return x <= East;
        }
    }
}

/// <summary>An entry of the album filter.</summary>
public sealed record AlbumChoice(string Label, Guid? AlbumId, Brush? Dot)
{
    public bool HasDot => Dot is not null;
}
