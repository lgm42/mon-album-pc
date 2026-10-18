using System.Windows;
using System.Windows.Controls;

namespace MonAlbumPc.App.Pages;

/// <summary>Viewer. Only its empty state exists for now.</summary>
public partial class PhotosPage : UserControl
{
    private readonly MainWindow _window;

    public PhotosPage(MainWindow window)
    {
        _window = window;
        InitializeComponent();
    }

    private void OnAddPhone(object sender, RoutedEventArgs e) => _window.ShowAddPhone(null);
}
