using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace MonAlbumPc.App.Pages;

public partial class SettingsPage : UserControl, IRefreshable
{
    private readonly AppHost _host;
    private bool _loading;

    public SettingsPage(AppHost host)
    {
        _host = host;
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Mon Album PC {version?.ToString(3)}";
    }

    public void Refresh()
    {
        _loading = true;
        AutostartToggle.IsChecked = MonAlbumPc.App.Autostart.IsEnabled;
        _loading = false;

        if (_host.ServerError is { } error)
        {
            NetworkTitle.Text = "Les téléphones ne peuvent pas joindre ce PC";
            NetworkDetail.Text = error;
        }
        else
        {
            NetworkTitle.Text = "Prêt à recevoir les photos";
            NetworkDetail.Text = _host.LanAddress is { } address
                ? $"Adresse {address} · ports {_host.ApiPort} et {_host.DownloadPort}"
                : "Ce PC n'est connecté à aucun réseau.";
        }
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            MonAlbumPc.App.Autostart.Set(AutostartToggle.IsChecked == true);
        }
    }

    private void OnOpenGitHub(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://github.com/lgm42/mon-album-pc") { UseShellExecute = true });
}
