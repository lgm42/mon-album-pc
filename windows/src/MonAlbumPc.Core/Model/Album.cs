namespace MonAlbumPc.Core.Model;

/// <summary>A person on this PC ("Marie") and the folder their photos go to.</summary>
public sealed record Album(Guid Id, string Name, string DestinationRoot, string Color, DateTimeOffset CreatedAt)
{
    /// <summary>Colors given to new albums, in order.</summary>
    public static readonly IReadOnlyList<string> Palette = ["#3D4FD1", "#8764B8", "#CA5010", "#038387", "#C239B3", "#498205"];
}
