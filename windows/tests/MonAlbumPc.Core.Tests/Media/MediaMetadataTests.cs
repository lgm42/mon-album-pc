using System.Buffers.Binary;
using System.Text;
using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class MediaMetadataTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Reads_gps_position_and_date_of_a_photo()
    {
        var path = _temp.WriteFile("IMG_1.jpg", TestImages.GeotaggedJpeg(48.8584, 2.2945, "2024:05:12 10:30:15"));

        var info = MediaMetadata.Read(path);

        Assert.Equal(new DateTime(2024, 5, 12, 10, 30, 15), info.TakenAt);
        Assert.NotNull(info.Location);
        Assert.Equal(48.8584, info.Location.Value.Latitude, 4);
        Assert.Equal(2.2945, info.Location.Value.Longitude, 4);
    }

    [Fact]
    public void Southern_and_western_references_give_negative_degrees()
    {
        var path = _temp.WriteFile("rio.jpg", TestImages.GeotaggedJpeg(-22.9519, -43.2105));

        var location = MediaMetadata.Read(path).Location;

        Assert.Equal(-22.9519, location!.Value.Latitude, 4);
        Assert.Equal(-43.2105, location.Value.Longitude, 4);
    }

    [Fact]
    public void Zero_position_written_without_a_fix_is_ignored()
    {
        var path = _temp.WriteFile("nofix.jpg", TestImages.GeotaggedJpeg(0, 0, "2024:05:12 10:30:15"));

        var info = MediaMetadata.Read(path);

        Assert.Null(info.Location);
        Assert.NotNull(info.TakenAt);
    }

    [Fact]
    public void Photo_without_gps_or_unreadable_file_has_no_position()
    {
        Assert.Null(MediaMetadata.Read(_temp.WriteFile("plain.jpg", TestImages.Jpeg("2024:05:12 10:30:15"))).Location);
        Assert.Equal(default, MediaMetadata.Read(_temp.WriteFile("broken.jpg", [1, 2, 3])));
    }

    [Theory]
    [InlineData("+48.8584+002.2945/", 48.8584, 2.2945)]
    [InlineData("+48.8584+002.2945+035.000/", 48.8584, 2.2945)]
    [InlineData("-33.8568+151.2153/", -33.8568, 151.2153)]
    [InlineData("+4851.504-00017.670/", 48.8584, -0.2945)]
    public void Parses_iso_6709_positions(string text, double latitude, double longitude)
    {
        var point = MediaMetadata.ParseIso6709(text);

        Assert.Equal(latitude, point!.Value.Latitude, 4);
        Assert.Equal(longitude, point.Value.Longitude, 4);
    }

    [Theory]
    [InlineData("+00.0000+000.0000/")]
    [InlineData("+95.0000+002.0000/")]
    [InlineData("+48.0000+190.0000/")]
    [InlineData("nowhere")]
    public void Rejects_impossible_iso_6709_positions(string text) => Assert.Null(MediaMetadata.ParseIso6709(text));

    [Fact]
    public void Reads_the_android_video_location_atom()
    {
        var path = _temp.WriteFile("VID_1.mp4", Mp4WithXyz("+45.7640+004.8357/"));

        var location = MediaMetadata.Read(path).Location;

        Assert.Equal(45.764, location!.Value.Latitude, 4);
        Assert.Equal(4.8357, location.Value.Longitude, 4);
    }

    /// <summary>Minimal MP4 layout: ftyp, then moov/udta/©xyz as written by Android's MediaMuxer.</summary>
    private static byte[] Mp4WithXyz(string iso6709)
    {
        var text = Encoding.ASCII.GetBytes(iso6709);
        var payload = new byte[4 + text.Length];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)text.Length);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), 0x15C7);
        text.CopyTo(payload, 4);
        var xyz = Atom([0xA9, (byte)'x', (byte)'y', (byte)'z'], payload);
        var moov = Atom("moov"u8.ToArray(), Atom("udta"u8.ToArray(), xyz));
        var ftyp = Atom("ftyp"u8.ToArray(), [.. "mp42"u8.ToArray(), 0, 0, 0, 0, .. "isommp42"u8.ToArray()]);
        return [.. ftyp, .. moov];
    }

    private static byte[] Atom(byte[] type, byte[] payload)
    {
        var atom = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(atom, (uint)atom.Length);
        type.CopyTo(atom, 4);
        payload.CopyTo(atom, 8);
        return atom;
    }
}
