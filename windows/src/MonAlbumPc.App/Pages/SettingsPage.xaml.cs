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

        ApkVersion.Text = _host.Apk is { } apk
            ? $"Version fournie avec ce PC : {apk.VersionName}"
            : "Aucune version de l'app n'est fournie avec ce PC.";
        ApkQrButton.IsEnabled = _host.Apk is not null && _host.ServerError is null && _host.LanAddress is not null;
    }

    private void OnShowApkQr(object sender, RoutedEventArgs e)
    {
        if (_host.Apk is { } apk && _host.LanAddress is { } address)
        {
            var url = $"http://{address}:{_host.DownloadPort}/";
            new Phones.DownloadQrDialog(url, apk.VersionName) { Owner = Window.GetWindow(this) }.ShowDialog();
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
