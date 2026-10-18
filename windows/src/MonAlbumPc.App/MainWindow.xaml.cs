using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MonAlbumPc.App.Albums;
using MonAlbumPc.App.History;
using MonAlbumPc.App.Pages;
using MonAlbumPc.App.Phones;

namespace MonAlbumPc.App;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly Dictionary<string, FrameworkElement> _pages = [];
    private bool _syncingSelection;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        UpdateStatus();
        MainNav.SelectedIndex = 0;
        _host.Core.Sessions.Changed += OnSessionsChanged;
        _host.Core.Sessions.Ended += OnSessionEnded;
    }

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
                "albums" => new AlbumsPage(_host, this),
                "history" => new HistoryPage(_host.Core),
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

    // A finished backup changes counts and history: reload the visible page.
    private void OnSessionEnded(Core.Storage.BackupSession session) => Dispatcher.BeginInvoke(() =>
    {
        if (Page.Content is IRefreshable page && IsVisible)
        {
            page.Refresh();
        }
    });

    private void UpdateStatus()
    {
        StatusProgress.Visibility = Visibility.Collapsed;
        if (_host.Core.Sessions.Active.FirstOrDefault() is { } active)
        {
            StatusIcon.Text = "";
            StatusIcon.SetResourceReference(ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            StatusTitle.Text = "Réception en cours";
            var phone = _host.Core.Devices.Get(active.DeviceId)?.Name ?? "Téléphone";
            var received = active.Photos + active.Videos;
            StatusDetail.Text = received == 0 ? phone : $"{phone} · {Formatting.Plural(received, "élément reçu", "éléments reçus")}";
            StatusProgress.Visibility = Visibility.Visible;
            return;
        }
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
