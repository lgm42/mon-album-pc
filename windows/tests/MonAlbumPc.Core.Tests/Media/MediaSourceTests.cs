using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class MediaSourceTests
{
    [Theory]
    [InlineData("Appareil photo", MediaSourceKind.Camera)]
    [InlineData("Camera", MediaSourceKind.Camera)]
    [InlineData("DCIM/Camera", MediaSourceKind.Camera)]
    [InlineData("DCIM/100ANDRO", MediaSourceKind.Camera)]
    [InlineData("WhatsApp Images", MediaSourceKind.WhatsApp)]
    [InlineData("WhatsApp Video", MediaSourceKind.WhatsApp)]
    [InlineData("WhatsApp Business Images", MediaSourceKind.WhatsApp)]
    [InlineData("WhatsApp Business", MediaSourceKind.WhatsApp)]
    [InlineData("Photos envoyées WhatsApp", MediaSourceKind.WhatsApp)]
    [InlineData("Android/media/com.whatsapp/WhatsApp/Media/WhatsApp Images", MediaSourceKind.WhatsApp)]
    [InlineData("Captures d'écran", MediaSourceKind.Screenshots)]
    [InlineData("Captures d’écran", MediaSourceKind.Screenshots)]
    [InlineData("Screenshots", MediaSourceKind.Screenshots)]
    [InlineData("Pictures/Screenshots", MediaSourceKind.Screenshots)]
    [InlineData("Telegram", MediaSourceKind.Other)]
    [InlineData("Téléchargements", MediaSourceKind.Other)]
    public void Known_folder_gives_the_provenance(string folder, MediaSourceKind kind)
    {
        // The name says camera: the folder wins.
        Assert.Equal(kind, MediaSource.Classify(folder, "IMG_20231126_101010.jpg").Kind);
    }

    [Fact]
    public void Other_folders_keep_their_name_for_display_and_as_keyword()
    {
        var source = MediaSource.Classify("Telegram", "photo.jpg");

        Assert.Equal("Telegram", source.Label);
        Assert.Equal("Telegram", source.Keyword);
    }

    [Theory]
    [InlineData("IMG-20231126-WA0007.jpg", MediaSourceKind.WhatsApp)]
    [InlineData("VID-20231126-WA0002.mp4", MediaSourceKind.WhatsApp)]
    [InlineData("img-20231126-wa0007_1.jpg", MediaSourceKind.WhatsApp)]
    [InlineData("Screenshot_20231126-101010.png", MediaSourceKind.Screenshots)]
    [InlineData("Screenshot_20231126-101010_WhatsApp.jpg", MediaSourceKind.Screenshots)]
    [InlineData("IMG_20231126_101010.jpg", MediaSourceKind.Camera)]
    [InlineData("PXL_20231126_101010123.jpg", MediaSourceKind.Camera)]
    [InlineData("20231126_101010.jpg", MediaSourceKind.Camera)]
    [InlineData("vacances.jpg", MediaSourceKind.Other)]
    public void Without_folder_the_name_tells(string name, MediaSourceKind kind)
    {
        Assert.Equal(kind, MediaSource.Classify(null, name).Kind);
        Assert.Equal(kind, MediaSource.Classify("", @"D:\Photos\2023\2023-11\" + name).Kind);
    }

    [Fact]
    public void Unknown_provenance_has_no_keyword()
    {
        Assert.Null(MediaSource.Classify(null, "vacances.jpg").Keyword);
        Assert.Equal("Autres", MediaSource.Classify(null, "vacances.jpg").Label);
    }

    [Theory]
    [InlineData("Appareil photo", null)]
    [InlineData("WhatsApp Images", "WhatsApp")]
    [InlineData("Captures d'écran", "Capture d'écran")]
    public void Keywords_name_the_provenance(string folder, string? keyword) =>
        Assert.Equal(keyword, MediaSource.Classify(folder, "a.jpg").Keyword);

    [Theory]
    [InlineData("WhatsApp Images/IMG-20231126-WA0007.jpg", "WhatsApp Images")]
    [InlineData("DCIM/Camera/a.jpg", "DCIM/Camera")]
    [InlineData("a.jpg", null)]
    [InlineData(null, null)]
    public void Folder_of_a_source_path(string? sourcePath, string? folder) =>
        Assert.Equal(folder, MediaSource.FolderOf(sourcePath));
}
