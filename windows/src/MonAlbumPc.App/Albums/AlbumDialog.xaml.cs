using System.Windows;
using Microsoft.Win32;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.App.Albums;

/// <summary>"Nouvel album" / "Modifier l'album": a name and the folder the photos go to.</summary>
public partial class AlbumDialog : Window
{
    private readonly CoreServices _core;
    private readonly Album? _album;
    private bool _folderChosen;

    public AlbumDialog(CoreServices core, Album? album)
    {
        _core = core;
        _album = album;
        InitializeComponent();
        Title = album is null ? "Nouvel album" : "Modifier l'album";
        Heading.Text = Title;
        SaveButton.Content = album is null ? "Créer l'album" : "Enregistrer";
        NameBox.Text = album?.Name ?? "";
        FolderBox.Text = album?.DestinationRoot ?? "";
        Loaded += (_, _) => NameBox.Focus();
        Update();
    }

    /// <summary>The created or updated album.</summary>
    public Album? Result { get; private set; }

    /// <summary>Folder proposed for a new album: next to the existing albums, or in the Pictures folder.</summary>
    public static string SuggestFolder(CoreServices core, string name)
    {
        var parent = core.Albums.List().Select(a => Path.GetDirectoryName(a.DestinationRoot)).FirstOrDefault(p => p is not null)
                     ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return string.IsNullOrWhiteSpace(name) ? parent : Path.Combine(parent, name.Trim());
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Dossier de destination",
            InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : SuggestFolder(_core, ""),
        };
        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
            _folderChosen = true;
            Update();
        }
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_album is null && !_folderChosen)
        {
            FolderBox.Text = string.IsNullOrWhiteSpace(NameBox.Text) ? "" : SuggestFolder(_core, NameBox.Text);
        }
        Update();
    }

    private void Update()
    {
        var name = NameBox.Text.Trim();
        var folder = FolderBox.Text.Trim();
        SaveButton.IsEnabled = name.Length > 0 && folder.Length > 0;
        Preview.Text = folder.Length > 0
            ? DestinationLayout.TargetDirectory(folder, DateTime.Today) + Path.DirectorySeparatorChar
            : "";
        var moved = _album is not null && !string.Equals(_album.DestinationRoot, folder, StringComparison.OrdinalIgnoreCase);
        FolderWarning.Visibility = moved ? Visibility.Visible : Visibility.Collapsed;
        if (moved)
        {
            FolderWarningText.Text = $"Les photos déjà importées restent dans {_album!.DestinationRoot}. " +
                                     "Seules les prochaines photos iront dans le nouveau dossier.";
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var folder = FolderBox.Text.Trim();
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossible de créer le dossier {folder}.\n\n{ex.Message}", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_album is null)
        {
            Result = new Album(Guid.NewGuid(), name, folder, _core.Albums.NextColor(), _core.Clock.GetLocalNow());
            _core.Albums.Add(Result);
        }
        else
        {
            Result = _album with { Name = name, DestinationRoot = folder };
            _core.Albums.Update(Result);
        }
        DialogResult = true;
    }
}
