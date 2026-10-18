using MonAlbumPc.Core.Import;

namespace MonAlbumPc.Core.Tests.Import;

public sealed class DestinationLayoutTests
{
    [Fact]
    public void Files_go_to_year_then_month_folders() =>
        Assert.Equal(@"D:\Photos\Marie\2026\2026-10", DestinationLayout.TargetDirectory(@"D:\Photos\Marie", new DateTime(2026, 10, 17)));

    [Fact]
    public void Name_collisions_get_a_numbered_suffix() =>
        Assert.Equal(
            [@"D:\x\IMG.jpg", @"D:\x\IMG_1.jpg", @"D:\x\IMG_2.jpg"],
            DestinationLayout.Candidates(@"D:\x", "IMG.jpg").Take(3));
}
