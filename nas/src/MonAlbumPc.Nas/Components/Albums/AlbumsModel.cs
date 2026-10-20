using MonAlbumPc.Core;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Nas.Components.Shared;

namespace MonAlbumPc.Nas.Components.Albums;

/// <summary>Data of the "Albums et téléphones" page (same rules as the Windows AlbumsViewModel).</summary>
public static class AlbumsModel
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

    public static List<AlbumCardModel> Load(CoreServices core)
    {
        var now = core.Clock.GetLocalNow();
        return core.Albums.List().Select(album =>
        {
            var phones = core.Devices.ListByAlbum(album.Id)
                .Select(d => PhoneRowModel.Create(d, core.History.LastImportByDevice(d.Id), core.History.CountByDevice(d.Id), now))
                .ToList();
            return new AlbumCardModel(album, core.History.CountByAlbum(album.Id), phones);
        }).ToList();
    }
}

public sealed record AlbumCardModel(Album Album, long PhotoCount, IReadOnlyList<PhoneRowModel> Phones)
{
    public string Initial => Formatting.Initial(Album.Name);

    public string Caption =>
        $"{Formatting.Plural(PhotoCount, "photo", "photos")} · " +
        (Phones.Count == 0 ? "aucun téléphone" : Formatting.Plural(Phones.Count, "téléphone", "téléphones"));

    public bool CanDelete => Phones.Count == 0;

    public string DeleteTooltip => CanDelete
        ? "Supprimer l'album"
        : "Impossible de supprimer un album qui a encore des téléphones. Dissociez-les d'abord.";
}

public sealed record PhoneRowModel(Device Device, string LastBackup, bool IsStale, string Received)
{
    public static PhoneRowModel Create(Device device, DateTimeOffset? lastBackup, long received, DateTimeOffset now) => new(
        device,
        Formatting.Ago(lastBackup, now),
        lastBackup is null ? now - device.PairedAt > AlbumsModel.StaleAfter : now - lastBackup > AlbumsModel.StaleAfter,
        Formatting.Count(received));
}
