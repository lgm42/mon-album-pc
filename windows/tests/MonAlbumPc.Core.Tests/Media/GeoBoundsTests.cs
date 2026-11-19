using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class GeoBoundsTests
{
    private static readonly GeoBounds France = new(41.3, -5.2, 51.1, 9.6);

    [Theory]
    [InlineData(48.8584, 2.2945, true)] // Paris
    [InlineData(43.2965, 5.3698, true)] // Marseille
    [InlineData(51.1, 9.6, true)] // on the edge
    [InlineData(40.4168, -3.7038, false)] // Madrid: too far south
    [InlineData(48.2082, 16.3738, false)] // Vienna: too far east
    public void Contains_points_inside_the_area(double latitude, double longitude, bool expected) =>
        Assert.Equal(expected, France.Contains(latitude, longitude));

    [Fact]
    public void Wraps_across_the_date_line()
    {
        var pacific = new GeoBounds(-30, 170, 10, 190);

        Assert.True(pacific.Contains(-17.7134, 178.065)); // Fiji
        Assert.True(pacific.Contains(-13.8333, -171.75)); // Samoa, written as -171.75°
        Assert.False(pacific.Contains(-17.5, 160));
        Assert.False(pacific.Contains(-17.5, -160));
    }

    [Fact]
    public void Follows_the_map_one_world_further()
    {
        var copy = new GeoBounds(41.3, 354.8, 51.1, 369.6); // France on the next copy of the world

        Assert.True(copy.Contains(48.8584, 2.2945));
        Assert.False(copy.Contains(40.4168, 30));
    }

    [Fact]
    public void A_view_wider_than_the_world_keeps_every_longitude()
    {
        var world = new GeoBounds(-85, -400, 85, 20);

        Assert.True(world.Contains(0, 179.9));
        Assert.True(world.Contains(0, -179.9));
        Assert.False(world.Contains(89, 0));
    }
}
