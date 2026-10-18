using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MonAlbumPc.Core.Media;

namespace MonAlbumPc.App.Photos;

/// <summary>A cell of the grid. Its thumbnail is decoded the first time the cell is shown.</summary>
public sealed partial class PhotoTile(LibraryItem item, int thumbnailPixels) : ObservableObject
{
    private bool _requested;

    public LibraryItem Item { get; } = item;
    public string Name => Path.GetFileName(Item.Path);
    public bool IsVideo => Item.IsVideo;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    private ImageSource? _thumbnail;

    public ImageSource? Thumbnail
    {
        get
        {
            if (!_requested)
            {
                _requested = true;
                _ = LoadAsync();
            }
            return _thumbnail;
        }
    }

    /// <summary>Drops the thumbnail (after a rotation) so it is decoded again.</summary>
    public void Reload()
    {
        _requested = false;
        _thumbnail = null;
        OnPropertyChanged(nameof(Thumbnail));
    }

    private async Task LoadAsync()
    {
        _thumbnail = await Thumbnails.LoadAsync(Item.Path, Item.IsVideo, thumbnailPixels);
        OnPropertyChanged(nameof(Thumbnail));
    }
}

/// <summary>Rows of the virtualized grid: a month title, or a line of tiles.</summary>
public abstract record GridRow;

public sealed record MonthRow(string Title, string Count) : GridRow;

public sealed record TilesRow(IReadOnlyList<PhotoTile> Tiles, double Size) : GridRow;

/// <summary>An entry of the left filter pane.</summary>
public sealed record FilterEntry(string Label, int Count, Brush? Dot, Guid? AlbumId, DateTime? Month, bool IsYear)
{
    public string CountText => Formatting.Count(Count);
    public bool HasDot => Dot is not null;
}
