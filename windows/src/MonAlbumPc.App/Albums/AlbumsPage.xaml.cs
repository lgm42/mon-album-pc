using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace MonAlbumPc.App.Albums;

public partial class AlbumsPage : UserControl, IRefreshable
{
    private readonly AppHost _host;
    private readonly MainWindow _window;
    private readonly AlbumsViewModel _model;

    public AlbumsPage(AppHost host, MainWindow window)
    {
        _host = host;
        _window = window;
        _model = new AlbumsViewModel(host.Core);
        DataContext = _model;
        InitializeComponent();
    }

    public void Refresh() => _model.Refresh();

    private void OnNewAlbum(object sender, RoutedEventArgs e)
    {
        if (new AlbumDialog(_host.Core, null) { Owner = _window }.ShowDialog() == true)
        {
            Refresh();
        }
    }

    private void OnEditAlbum(object sender, RoutedEventArgs e)
    {
        if (new AlbumDialog(_host.Core, Card(sender).Album) { Owner = _window }.ShowDialog() == true)
        {
            Refresh();
        }
    }

    private void OnDeleteAlbum(object sender, RoutedEventArgs e)
    {
        var album = Card(sender).Album;
        if (Dialogs.Confirm(_window, $"Supprimer l'album {album.Name} ?", "Le dossier de l'album et ses photos sont conservés.",
                "Supprimer", album.DestinationRoot, destructive: true))
        {
            _host.Core.Albums.Delete(album.Id);
            Refresh();
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var folder = Card(sender).Folder;
        if (Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        else
        {
            Dialogs.Inform(_window, "Dossier indisponible", "Ce dossier est introuvable : le disque est peut-être débranché.", folder);
        }
    }

    private void OnAddPhone(object sender, RoutedEventArgs e) => _window.ShowAddPhone(null);

    private void OnAddPhoneToAlbum(object sender, RoutedEventArgs e) => _window.ShowAddPhone(Card(sender).Album.Id);

    private void OnPhoneMenu(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.ContextMenu!.DataContext = button.Tag;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private void OnUnpair(object sender, RoutedEventArgs e)
    {
        var phone = (PhoneRow)((FrameworkElement)sender).DataContext;
        if (Dialogs.Confirm(_window, $"Dissocier {phone.Name} ?",
                "Ce téléphone ne sera plus sauvegardé sur ce PC. Les photos déjà reçues restent dans le dossier de l'album. " +
                "Pour le relier à nouveau, il faudra rescanner un QR code.",
                "Dissocier", destructive: true))
        {
            _host.Core.Devices.Revoke(phone.Device.Id, _host.Core.Clock.GetLocalNow());
            Refresh();
        }
    }

    private static AlbumCard Card(object sender) => (AlbumCard)((FrameworkElement)sender).Tag;
}
