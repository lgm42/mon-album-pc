using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.App.Albums;

public sealed partial class AlbumsViewModel(CoreServices core) : ObservableObject
{
    public ObservableCollection<AlbumCard> Albums { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public void Refresh()
    {
        Albums.Clear();
        var now = core.Clock.GetLocalNow();
        foreach (var album in core.Albums.List())
        {
            var phones = core.Devices.ListByAlbum(album.Id)
                .Select(d => new PhoneRow(d, core.History.LastImportByDevice(d.Id), core.History.CountByDevice(d.Id), now))
                .ToList();
            Albums.Add(new AlbumCard(album, core.History.CountByAlbum(album.Id), phones));
        }
        IsEmpty = Albums.Count == 0;
    }
}

public sealed class AlbumCard(Album album, long photoCount, IReadOnlyList<PhoneRow> phones)
{
    public Album Album { get; } = album;
    public string Name => Album.Name;
    public string Initial => Album.Name.Length > 0 ? Album.Name[..1].ToUpperInvariant() : "?";
    public Brush Color { get; } = (Brush)new BrushConverter().ConvertFromString(album.Color)!;
    public string Folder => Album.DestinationRoot;
    public string Caption { get; } =
        $"{Formatting.Plural(photoCount, "photo", "photos")} · " +
        (phones.Count == 0 ? "aucun téléphone" : Formatting.Plural(phones.Count, "téléphone", "téléphones"));
    public IReadOnlyList<PhoneRow> Phones { get; } = phones;
    public bool HasPhones => Phones.Count > 0;
    public bool CanDelete => !HasPhones;
    public string DeleteTooltip => CanDelete
        ? "Supprimer l'album"
        : "Impossible de supprimer un album qui a encore des téléphones. Dissociez-les d'abord.";
}

public sealed class PhoneRow(Device device, DateTimeOffset? lastBackup, long received, DateTimeOffset now)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

    public Device Device { get; } = device;
    public string Name => Device.Name;
    public string Model => Device.Model;
    public string LastBackup { get; } = Formatting.Ago(lastBackup, now);
    public bool IsStale { get; } = lastBackup is null ? now - device.PairedAt > StaleAfter : now - lastBackup > StaleAfter;
    public string Received { get; } = Formatting.Count(received);
}
