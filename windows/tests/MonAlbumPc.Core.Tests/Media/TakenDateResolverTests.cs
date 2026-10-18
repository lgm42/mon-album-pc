using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class TakenDateResolverTests : IDisposable
{
    private static readonly DateTime FileDate = new(2026, 1, 2, 3, 4, 5);
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("IMG_20261017_131705.jpg", "2026-10-17 13:17:05")]
    [InlineData("PXL_20261017_131705123.jpg", "2026-10-17 13:17:05")]
    [InlineData("Screenshot_20261017-131705_Chrome.png", "2026-10-17 13:17:05")]
    [InlineData("VID20261017131705.mp4", "2026-10-17 13:17:05")]
    [InlineData("2026-10-17 13.17.05.jpg", "2026-10-17 13:17:05")]
    [InlineData("Screenshot_2026-10-17-13-17-05-123.png", "2026-10-17 13:17:05")]
    [InlineData("IMG-20240512-WA0003.jpg", "2024-05-12 00:00:00")]
    [InlineData("VID-20240512-WA0010.mp4", "2024-05-12 00:00:00")]
    public void Parses_dates_from_phone_file_names(string name, string expected) =>
        Assert.Equal(DateTime.Parse(expected), TakenDateResolver.ParseFileName(name));

    [Theory]
    [InlineData("holiday.jpg")]
    [InlineData("IMG_20261317_131705.jpg")]
    [InlineData("IMG_1234.jpg")]
    [InlineData("12345678901234.jpg")]
    public void Ignores_names_without_a_valid_date(string name) =>
        Assert.Null(TakenDateResolver.ParseFileName(name));

    [Fact]
    public void Exif_wins_over_file_name()
    {
        var path = _temp.WriteFile("IMG_20261017_131705.jpg", TestImages.Jpeg("2024:05:12 10:30:15"));

        var result = TakenDateResolver.Resolve(path, "IMG_20261017_131705.jpg", null, FileDate);

        Assert.Equal(new TakenDate(new DateTime(2024, 5, 12, 10, 30, 15), TakenDateSource.Exif), result);
    }

    [Fact]
    public void File_name_is_used_without_exif()
    {
        var path = _temp.WriteFile("upload.part", TestImages.Jpeg());

        var result = TakenDateResolver.Resolve(path, "IMG-20240512-WA0003.jpg", new DateTime(2023, 1, 1), FileDate);

        Assert.Equal(TakenDateSource.FileName, result.Source);
        Assert.Equal(new DateTime(2024, 5, 12), result.Value);
    }

    [Fact]
    public void Phone_date_then_file_date_are_last_resorts()
    {
        var path = _temp.WriteFile("a.part", TestImages.Jpeg());

        Assert.Equal(
            new TakenDate(new DateTime(2023, 1, 1, 8, 0, 0), TakenDateSource.Phone),
            TakenDateResolver.Resolve(path, "holiday.jpg", new DateTime(2023, 1, 1, 8, 0, 0, 500), FileDate));
        Assert.Equal(
            new TakenDate(FileDate, TakenDateSource.FileDate),
            TakenDateResolver.Resolve(path, "holiday.jpg", null, FileDate));
    }

    [Fact]
    public void Unreadable_files_fall_back_without_throwing()
    {
        var path = _temp.WriteFile("broken.jpg", [1, 2, 3]);
        Assert.Equal(TakenDateSource.FileDate, TakenDateResolver.Resolve(path, "broken.jpg", null, FileDate).Source);
    }
}
