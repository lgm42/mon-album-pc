using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MonAlbumPc.Core.Library;

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
        var viewer = _host.IsViewer;
        ServerSections.Visibility = viewer ? Visibility.Collapsed : Visibility.Visible;
        UseLibraryButton.Visibility = viewer ? Visibility.Collapsed : Visibility.Visible;
        ChangeLibraryButton.Visibility = LeaveLibraryButton.Visibility = viewer ? Visibility.Visible : Visibility.Collapsed;
        if (viewer)
        {
            AutostartHint.Text = "L'app démarre réduite dans la zone de notification pour vous prévenir des nouvelles photos.";
        }
    }

    public void Refresh()
    {
        _loading = true;
        AutostartToggle.IsChecked = MonAlbumPc.App.Autostart.IsEnabled;
        _loading = false;

        if (_host.Library is { } library)
        {
            LibraryTitle.Text = library.State == LibraryState.Unavailable ? "Bibliothèque indisponible" : "Mode Visionneuse";
            LibraryDetail.Text = library.Root;
            return;
        }
        LibraryTitle.Text = "Ce PC reçoit lui-même les photos";
        LibraryDetail.Text = "Si un NAS les reçoit à sa place, ce PC peut simplement les consulter, et vous prévenir des nouvelles sauvegardes.";

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

    private async void OnChooseLibrary(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        var dialog = new OpenFolderDialog { Title = "Dossier de la bibliothèque sur le NAS" };
        if (_host.Settings.LibraryRoot is { Length: > 0 } current)
        {
            dialog.InitialDirectory = current;
        }
        if (dialog.ShowDialog(window) != true)
        {
            return;
        }
        var folder = dialog.FolderName;
        // A share that does not answer can take a while to say so.
        if (await Task.Run(() => LibraryManifest.Read(folder)) is null)
        {
            MessageBox.Show(window,
                $"Ce dossier n'est pas une bibliothèque Mon Album PC :\n{folder}\n\nLe fichier .monalbumpc\\library.json est introuvable ou illisible. " +
                "Choisissez le dossier qui contient les dossiers des albums, et vérifiez que le NAS a bien démarré.",
                "Bibliothèque", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var consequence = _host.IsViewer
            ? "Mon Album PC va redémarrer et afficher cette bibliothèque."
            : "Ce PC ne recevra plus les photos des téléphones : c'est le NAS qui les reçoit. Votre catalogue est conservé, " +
              "vous pourrez revenir au mode autonome à tout moment.\n\nMon Album PC va redémarrer.";
        var answer = MessageBox.Show(window, $"Utiliser cette bibliothèque ?\n{folder}\n\n{consequence}", "Bibliothèque",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK)
        {
            SwitchTo(new AppSettings(AppMode.Viewer, folder));
        }
    }

    private void OnLeaveLibrary(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(Window.GetWindow(this),
            "Ce PC recevra à nouveau lui-même les photos des téléphones qui lui sont associés. Les photos du NAS n'y sont pas touchées.\n\nMon Album PC va redémarrer.",
            "Revenir au mode autonome", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK)
        {
            SwitchTo(_host.Settings with { Mode = AppMode.Autonomous });
        }
    }

    private void SwitchTo(AppSettings settings)
    {
        try
        {
            settings.Save(_host.DataRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), $"Impossible d'enregistrer ce réglage.\n\n{ex.Message}", "Bibliothèque",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ((App)Application.Current).Restart();
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
