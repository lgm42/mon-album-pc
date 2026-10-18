using System.Windows;
using System.Windows.Controls;
using MonAlbumPc.Core;

namespace MonAlbumPc.App.History;

public partial class HistoryPage : UserControl, IRefreshable
{
    private readonly HistoryViewModel _model;
    private bool _loading;

    public HistoryPage(CoreServices core)
    {
        _model = new HistoryViewModel(core);
        InitializeComponent();
    }

    public void Refresh()
    {
        _loading = true;
        var album = (AlbumFilter.SelectedItem as FilterOption)?.Id;
        var phone = (PhoneFilter.SelectedItem as FilterOption)?.Id;
        _model.Load();
        AlbumFilter.ItemsSource = _model.Albums;
        AlbumFilter.SelectedItem = _model.Albums.FirstOrDefault(a => a.Id == album) ?? _model.Albums[0];
        PhoneFilter.ItemsSource = _model.Phones;
        PhoneFilter.SelectedItem = _model.Phones.FirstOrDefault(p => p.Id == phone) ?? _model.Phones[0];
        _loading = false;
        Apply();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            Apply();
        }
    }

    private void Apply()
    {
        var groups = _model.Groups(
            (AlbumFilter.SelectedItem as FilterOption)?.Id,
            (PhoneFilter.SelectedItem as FilterOption)?.Id,
            ErrorsOnly.IsChecked == true);
        Days.ItemsSource = groups;
        var filtered = AlbumFilter.SelectedIndex > 0 || PhoneFilter.SelectedIndex > 0 || ErrorsOnly.IsChecked == true;
        Empty.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = filtered ? "Aucune sauvegarde ne correspond à ces filtres." : "Aucune sauvegarde pour l'instant.";
        EmptyText.Text = filtered ? "" : "Chaque sauvegarde d'un téléphone apparaîtra ici.";
    }
}
