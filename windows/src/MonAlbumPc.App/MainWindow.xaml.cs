using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MonAlbumPc.App.Albums;
using MonAlbumPc.App.History;
using MonAlbumPc.App.Pages;
using MonAlbumPc.App.Phones;
using MonAlbumPc.Core.Activity;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Library;

namespace MonAlbumPc.App;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly Dictionary<string, FrameworkElement> _pages = [];
    private bool _syncingSelection;
    private bool _pageOutdated;

    // A silent phone raises no event: look again now and then to show that it is being waited for.
    private readonly DispatcherTimer _waitingCheck = new() { Interval = TimeSpan.FromSeconds(5) };

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        if (_host.Library is { } library)
        {
            // A viewer does not manage phones: only what shows the library stays.
            foreach (var item in MainNav.Items.Cast<ListBoxItem>().Where(i => (string)i.Tag is "albums" or "history").ToList())
            {
                MainNav.Items.Remove(item);
            }
            library.StateChanged += OnLibraryStateChanged;
            library.ContentChanged += OnLibraryContentChanged;
        }
        _waitingCheck.Tick += (_, _) => UpdateStatus();
        UpdateStatus();
        MainNav.SelectedIndex = 0;
        _host.Core.Sessions.Changed += OnSessionsChanged;
        _host.Core.Sessions.Ended += OnSessionEnded;
        // Raised at most twice a second, also at each look at the shared library ("vérifiée à 00:12").
        _host.Core.Work.Changed += OnWorkChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _pageOutdated)
            {
                RefreshPage();
            }
        };
    }

    /// <summary>The shared library in viewer mode, null otherwise.</summary>
    public LibraryViewer? Library => _host.Library;

    /// <summary>Opens the add-phone wizard, optionally for a given album.</summary>
    public void ShowAddPhone(Guid? albumId)
    {
        var dialog = new AddPhoneDialog(_host, albumId) { Owner = this };
        dialog.ShowDialog();
        Navigate(dialog.PairedAlbumId is null ? CurrentTag : "albums");
    }

    public void Navigate(string tag)
    {
        _syncingSelection = true;
        MainNav.SelectedItem = MainNav.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);
        FooterNav.SelectedItem = FooterNav.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);
        _syncingSelection = false;
        Show(tag);
    }

    private string CurrentTag => (MainNav.SelectedItem ?? FooterNav.SelectedItem) is ListBoxItem { Tag: string tag } ? tag : "photos";

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || sender is not ListBox { SelectedItem: ListBoxItem { Tag: string tag } })
        {
            return;
        }
        _syncingSelection = true;
        (sender == MainNav ? FooterNav : MainNav).SelectedItem = null;
        _syncingSelection = false;
        Show(tag);
    }

    private void Show(string tag)
    {
        if (!_pages.TryGetValue(tag, out var page))
        {
            page = tag switch
            {
                "map" => new Map.MapPage(_host.Core),
                "albums" => new AlbumsPage(_host, this),
                "history" => new HistoryPage(_host.Core),
                "activity" => new Activity.ActivityPage(_host.Core, _host.IsViewer),
                "settings" => new SettingsPage(_host),
                _ => new Photos.PhotosPage(_host.Core, this),
            };
            _pages[tag] = page;
        }
        if (page is IRefreshable refreshable)
        {
            refreshable.Refresh();
        }
        Page.Content = page;
        UpdateStatus();
    }

    private void OnSessionsChanged() => Dispatcher.BeginInvoke(UpdateStatus);

    private void OnWorkChanged() => Dispatcher.BeginInvoke(UpdateStatus);

    private void OnWorkLinesClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Navigate("activity");

    /// <summary>"Index des positions · 1 240 / 7 018 · IMG_1234.jpg" and its bar, for each task in progress.</summary>
    private void ShowWork()
    {
        WorkLines.Children.Clear();
        var running = _host.Core.Work.Running;
        foreach (var work in running.Take(3))
        {
            var line = WorkText.Compact(work);
            WorkLines.Children.Add(new TextBlock
            {
                Text = line,
                ToolTip = line,
                Style = (Style)FindResource("Caption"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0),
            });
            WorkLines.Children.Add(new ProgressBar
            {
                Height = 3,
                Margin = new Thickness(0, 4, 0, 0),
                Maximum = 1,
                Value = work.Fraction ?? 0,
                IsIndeterminate = work.Fraction is null,
            });
        }
        if (running.Count > 3)
        {
            WorkLines.Children.Add(new TextBlock
            {
                Text = $"et {running.Count - 3} autre(s)…",
                Style = (Style)FindResource("Caption"),
                Margin = new Thickness(0, 4, 0, 0),
            });
        }
        WorkLines.Visibility = running.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // A finished backup changes counts and history: reload the visible page.
    private void OnSessionEnded(Core.Storage.BackupSession session) => Dispatcher.BeginInvoke(RefreshPage);

    private void OnLibraryStateChanged() => Dispatcher.BeginInvoke(() =>
    {
        UpdateStatus();
        RefreshPage();
    });

    private void OnLibraryContentChanged() => Dispatcher.BeginInvoke(RefreshPage);

    // Hidden in the notification area, the app waits to be shown again rather than reading the share for nobody.
    private void RefreshPage()
    {
        _pageOutdated = !IsVisible;
        if (IsVisible && Page.Content is IRefreshable page)
        {
            page.Refresh();
        }
    }

    private void UpdateStatus()
    {
        StatusProgress.Visibility = Visibility.Collapsed;
        SetBar(FileProgress, null);
        SetLine(FileTransfer, null);
        SetLine(StatusCounts, null);
        SetLine(StatusError, null);
        ShowWork();
        if (_host.Library is { } library)
        {
            ShowLibraryStatus(library);
            return;
        }
        var core = _host.Core;
        var now = core.Clock.GetLocalNow();
        if (core.Sessions.Active.FirstOrDefault() is { } active)
        {
            var status = SessionStatusText.For(active, core.Devices.Get(active.DeviceId)?.Name ?? "Téléphone", now);
            StatusIcon.Text = status.Waiting ? "" : "";
            StatusIcon.SetResourceReference(ForegroundProperty, status.Waiting ? "SystemFillColorCautionBrush" : "AccentTextFillColorPrimaryBrush");
            StatusTitle.Text = status.Title;
            StatusDetail.Text = status.Detail;
            SetLine(FileTransfer, status.Transfer);
            SetLine(StatusCounts, status.Counts);
            SetLine(StatusError, status.Error);
            SetBar(FileProgress, status.FileFraction);
            StatusProgress.Visibility = status.Waiting || status.FileFraction is not null ? Visibility.Collapsed : Visibility.Visible;
            _waitingCheck.Start();
            return;
        }
        _waitingCheck.Stop();
        if (_host.ServerError is { } error)
        {
            StatusIcon.Text = "";
            StatusIcon.SetResourceReference(ForegroundProperty, "SystemFillColorCautionBrush");
            StatusTitle.Text = "Sauvegardes impossibles";
            StatusDetail.Text = error;
            return;
        }
        StatusIcon.Text = "";
        StatusIcon.SetResourceReference(ForegroundProperty, "SystemFillColorSuccessBrush");
        StatusTitle.Text = "Prêt à recevoir les photos";
        StatusDetail.Text = _host.LanAddress is { } address
            ? $"Adresse sur le réseau : {address}"
            : "Ce PC n'est connecté à aucun réseau.";
        if (core.Sessions.Last is { } last)
        {
            SetLine(StatusCounts, SessionStatusText.Summary(last, core.Devices.Get(last.Session.DeviceId)?.Name ?? "téléphone dissocié", now));
            SetLine(StatusError, last.Session.ErrorMessage);
        }
    }

    private static void SetLine(TextBlock block, string? text)
    {
        block.Text = text ?? "";
        block.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void SetBar(ProgressBar bar, double? fraction)
    {
        bar.Value = fraction ?? 0;
        bar.Visibility = fraction is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowLibraryStatus(LibraryViewer library)
    {
        var (icon, brush, title) = library.State switch
        {
            LibraryState.Available => ("\uE73E", "SystemFillColorSuccessBrush", "Bibliothèque connectée"),
            LibraryState.Unavailable => ("\uE7BA", "SystemFillColorCautionBrush", "Bibliothèque indisponible"),
            _ => ("\uE895", "AccentTextFillColorPrimaryBrush", "Connexion à la bibliothèque…"),
        };
        StatusIcon.Text = icon;
        StatusIcon.SetResourceReference(ForegroundProperty, brush);
        StatusTitle.Text = title;
        StatusDetail.Text = library.Root;
        StatusDetail.ToolTip = library.Root;
        var now = _host.Core.Clock.GetLocalNow();
        var checkedAt = library.LastChecked is { } at ? WorkText.Time(at, now) : null;
        if (library.State == LibraryState.Unavailable)
        {
            var since = library.UnavailableSince is { } from ? $"Injoignable depuis {WorkText.Time(from, now)}" : "Injoignable";
            SetLine(StatusCounts, checkedAt is null ? since : $"{since} · nouvel essai chaque minute, dernier à {checkedAt}");
        }
        else if (checkedAt is not null)
        {
            SetLine(StatusCounts, $"Bibliothèque à jour · vérifiée à {checkedAt}");
        }
        if (library.LastNews is { } news)
        {
            SetLine(FileTransfer, $"{WorkText.Time(news.At, now)} · {news.Notification.Title} : {news.Notification.Message}");
        }
    }

    // Closing the window keeps the app running in the notification area.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsQuitting)
        {
            e.Cancel = true;
            Hide();
        }
    }
}

/// <summary>A page that reloads its data each time it is shown.</summary>
public interface IRefreshable
{
    void Refresh();
}
