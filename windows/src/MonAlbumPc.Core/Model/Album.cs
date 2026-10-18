namespace MonAlbumPc.Core.Model;

/// <summary>A person on this PC ("Marie") and the folder their photos go to.</summary>
public sealed record Album(Guid Id, string Name, string DestinationRoot, string Color, DateTimeOffset CreatedAt)
{
    /// <summary>Colors given to new albums, in order (distinct from the indigo accent of the app).</summary>
    public static readonly IReadOnlyList<string> Palette = ["#0F6CBD", "#8764B8", "#CA5010", "#038387", "#C239B3", "#498205"];
}
