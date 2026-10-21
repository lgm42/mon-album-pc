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
        var answer = MessageBox.Show(_window,
            $"Supprimer l'album {album.Name} ?\n\nLe dossier {album.DestinationRoot} et ses photos sont conservés.",
            "Supprimer l'album", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
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
            MessageBox.Show(_window, $"Le dossier {folder} est introuvable : le disque est peut-être débranché.",
                "Dossier indisponible", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnAddPhone(object sender, RoutedEventArgs e) => _window.ShowAddPhone(null);

    private void OnAddPhoneToAlbum(object sender, RoutedEventArgs e) => _window.ShowAddPhone(Card(sender).Album.Id);

    private void OnPhoneMenu(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var phone = (PhoneRow)button.Tag;
        var menu = button.ContextMenu!;
        menu.DataContext = phone;

        // "Fusionner avec": the other phones of the same album.
        var merge = (MenuItem)menu.Items[0];
        merge.Items.Clear();
        var others = _model.Albums.First(a => a.Phones.Contains(phone)).Phones.Where(p => p != phone).ToList();
        foreach (var other in others)
        {
            var item = new MenuItem { Header = $"{other.Name} · {other.Model} · {other.LastBackup}", Tag = other };
            item.Click += (_, _) => Merge(phone, other);
            merge.Items.Add(item);
        }
        merge.IsEnabled = others.Count > 0;

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    /// <summary>Merges <paramref name="duplicate"/> into <paramref name="kept"/>: history moves, the duplicate disappears.</summary>
    private void Merge(PhoneRow duplicate, PhoneRow kept)
    {
        var answer = MessageBox.Show(_window,
            $"Fusionner « {duplicate.Name} » dans « {kept.Name} » ?\n\n" +
            $"Les {duplicate.Received} fichiers reçus et l'historique de « {duplicate.Name} » passent sur « {kept.Name} », " +
            $"puis « {duplicate.Name} » disparaît de la liste.\n\n" +
            $"Gardez la fiche du téléphone actuellement associé (dernière sauvegarde : {kept.LastBackup}). Aucune photo n'est supprimée.",
            "Fusionner des téléphones", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            _host.Core.Devices.Merge(duplicate.Device.Id, kept.Device.Id);
            Refresh();
        }
    }

    private void OnUnpair(object sender, RoutedEventArgs e)
    {
        var phone = (PhoneRow)((FrameworkElement)sender).DataContext;
        var answer = MessageBox.Show(_window,
            $"Dissocier {phone.Name} ?\n\nCe téléphone ne sera plus sauvegardé sur ce PC. Les photos déjà reçues restent dans le " +
            "dossier de l'album. Pour le relier à nouveau, il faudra rescanner un QR code.",
            "Dissocier le téléphone", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            _host.Core.Devices.Revoke(phone.Device.Id, _host.Core.Clock.GetLocalNow());
            Refresh();
        }
    }

    private static AlbumCard Card(object sender) => (AlbumCard)((FrameworkElement)sender).Tag;
}
