using System.Text;
using ImageMagick;
using MetadataExtractor;
using MetadataExtractor.Formats.Xmp;
using MonAlbumPc.Core.Media;
using static MonAlbumPc.Core.Tests.Media.XmpKeywordsTests;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class UserTagsTests : IDisposable
{
    private static readonly DateTime Modified = new(2023, 11, 26, 10, 12, 0, DateTimeKind.Utc);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Photo(byte[]? content = null, string name = "IMG_1.jpg")
    {
        var path = _temp.WriteFile(name, content ?? PhoneJpeg());
        File.SetLastWriteTimeUtc(path, Modified);
        return path;
    }

    [Theory]
    [InlineData(" boulot ", "boulot")]
    [InlineData("Bricolage / ESP32", "Bricolage/ESP32")]
    [InlineData("Bricolage|ESP32", "Bricolage/ESP32")]
    [InlineData("Travaux   maison//", "Travaux maison")]
    [InlineData(" / ", null)]
    public void Typed_tags_are_tidied(string typed, string? expected) => Assert.Equal(expected, UserTags.Normalize(typed));

    [Fact]
    public void A_hierarchical_tag_is_written_as_lightroom_and_digikam_read_it_without_touching_the_image()
    {
        var original = PhoneJpeg();
        var path = Photo(original);

        var result = UserTags.Change(path, ["Bricolage/ESP32"], []);

        Assert.True(result.Changed);
        Assert.Equal(["Bricolage/ESP32"], result.Tags);
        Assert.Equal(Modified, File.GetLastWriteTimeUtc(path));
        var tagged = File.ReadAllBytes(path);
        Assert.Equal(Segments(original).ImageData, Segments(tagged).ImageData);
        Assert.Equal(Segments(original).Headers.Select(Convert.ToHexString),
            Segments(tagged).Headers.Where(s => !s.AsSpan(4).StartsWith("http://ns.adobe.com/xap/1.0/\0"u8)).Select(Convert.ToHexString));
        var properties = ImageMetadataReader.ReadMetadata(path).OfType<XmpDirectory>().Single().GetXmpProperties();
        Assert.Equal("Bricolage|ESP32", properties["lr:hierarchicalSubject[1]"]);
        Assert.Equal(["ESP32", "Bricolage"], properties.Where(p => p.Key.StartsWith("dc:subject[")).Select(p => p.Value));
        Assert.Equal(["Bricolage/ESP32"], UserTags.Read(path));
        Assert.Single(System.IO.Directory.GetFiles(_temp.Path)); // no sidecar, no leftover
    }

    [Fact]
    public void Removing_a_tag_keeps_the_other_properties_the_provenance_and_the_image()
    {
        var path = Photo(JpegWithPacket("""
            <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:xmp="http://ns.adobe.com/xap/1.0/" xmp:Rating="4">
             <dc:subject><rdf:Bag><rdf:li>WhatsApp</rdf:li></rdf:Bag></dc:subject>
            </rdf:Description>
            """));
        var original = File.ReadAllBytes(path);
        UserTags.Change(path, ["boulot", "Travaux maison"], [], keep: "WhatsApp");

        var result = UserTags.Change(path, [], ["boulot"], keep: "WhatsApp");

        Assert.Equal(["Travaux maison"], result.Tags);
        Assert.Equal(Modified, File.GetLastWriteTimeUtc(path));
        Assert.Equal(Segments(original).ImageData, Segments(File.ReadAllBytes(path)).ImageData);
        Assert.Equal(["WhatsApp", "Travaux maison"], XmpKeywords.Read(path));
        var properties = ImageMetadataReader.ReadMetadata(path).OfType<XmpDirectory>().Single().GetXmpProperties();
        Assert.Equal("4", properties["xmp:Rating"]);
    }

    [Fact]
    public void Renaming_replaces_the_tag_in_one_rewrite()
    {
        var path = Photo();
        UserTags.Change(path, ["boulot", "Bricolage/ESP32"], []);

        var result = UserTags.Change(path, ["Travail"], ["boulot"]);

        Assert.Equal(["Bricolage/ESP32", "Travail"], result.Tags);
        Assert.DoesNotContain("boulot", XmpKeywords.Read(path));
    }

    [Fact]
    public void Removing_a_parent_tag_keeps_its_children()
    {
        var path = Photo();
        UserTags.Change(path, ["Bricolage", "Bricolage/ESP32"], []);
        Assert.Equal(["Bricolage", "Bricolage/ESP32"], UserTags.Read(path));

        var result = UserTags.Change(path, [], ["Bricolage"]);

        Assert.Equal(["Bricolage/ESP32"], result.Tags);
        Assert.Contains("Bricolage", XmpKeywords.Read(path)); // still the parent of ESP32 in dc:subject
    }

    [Fact]
    public void The_provenance_keyword_is_not_a_user_tag_but_keywords_of_other_tools_are()
    {
        var path = Photo(JpegWithPacket("""
            <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/">
             <dc:subject><rdf:Bag><rdf:li>Telegram</rdf:li><rdf:li>Vacances</rdf:li></rdf:Bag></dc:subject>
            </rdf:Description>
            """));

        Assert.Equal(["Vacances"], UserTags.Read(path, provenanceKeyword: "Telegram"));
        Assert.Equal(["Telegram", "Vacances"], UserTags.Read(path));
    }

    [Fact]
    public void Removing_a_user_tag_named_like_the_provenance_keeps_the_provenance()
    {
        var path = Photo();
        XmpKeywords.Add(path, "WhatsApp");
        UserTags.Change(path, ["WhatsApp"], [], keep: "WhatsApp");
        Assert.Equal(["WhatsApp"], UserTags.Read(path, "WhatsApp")); // chosen by the user: shown

        UserTags.Change(path, [], ["WhatsApp"], keep: "WhatsApp");

        Assert.Empty(UserTags.Read(path, "WhatsApp"));
        Assert.Equal(["WhatsApp"], XmpKeywords.Read(path));
    }

    [Fact]
    public void A_digikam_tag_list_follows_the_changes()
    {
        var path = Photo(JpegWithPacket("""
            <rdf:Description rdf:about="" xmlns:digiKam="http://www.digikam.org/ns/1.0/">
             <digiKam:TagsList><rdf:Seq><rdf:li>Famille/Léa</rdf:li></rdf:Seq></digiKam:TagsList>
            </rdf:Description>
            """));
        Assert.Equal(["Famille/Léa"], UserTags.Read(path));

        UserTags.Change(path, ["boulot"], ["Famille/Léa"]);

        Assert.Equal(["boulot"], UserTags.Read(path));
        var properties = ImageMetadataReader.ReadMetadata(path).OfType<XmpDirectory>().Single().GetXmpProperties();
        Assert.Equal("boulot", properties["digiKam:TagsList[1]"]);
    }

    [Fact]
    public void A_video_gets_its_tags_in_a_sidecar_named_after_the_whole_file_name()
    {
        byte[] video = [0, 0, 0, 8, (byte)'f', (byte)'t', (byte)'y', (byte)'p'];
        var path = Photo(video, "VID_1.mp4");

        var result = UserTags.Change(path, ["Travaux maison"], []);

        Assert.True(result.Changed);
        Assert.Equal(video, File.ReadAllBytes(path));
        Assert.Equal(Modified, File.GetLastWriteTimeUtc(path));
        var sidecar = Path.Combine(_temp.Path, "VID_1.mp4.xmp");
        Assert.Equal(sidecar, UserTags.FindSidecar(path));
        Assert.Contains("Travaux maison", File.ReadAllText(sidecar));
        Assert.Equal(["Travaux maison"], UserTags.Read(path));

        UserTags.Change(path, [], ["Travaux maison"]);
        Assert.Empty(UserTags.Read(path));
    }

    [Fact]
    public void A_lightroom_sidecar_already_there_is_the_one_updated()
    {
        var path = Photo([1, 2, 3], "VID_1.mov");
        var lightroom = _temp.WriteFile("VID_1.xmp", Encoding.UTF8.GetBytes("""
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
             <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
              <rdf:Description rdf:about="" xmlns:xmp="http://ns.adobe.com/xap/1.0/" xmp:Rating="5"/>
             </rdf:RDF>
            </x:xmpmeta>
            """));

        UserTags.Change(path, ["boulot"], []);

        Assert.False(File.Exists(UserTags.SidecarPath(path)));
        Assert.Contains("xmp:Rating=\"5\"", File.ReadAllText(lightroom));
        Assert.Equal(["boulot"], UserTags.Read(path));
    }

    [Fact]
    public void Removing_a_tag_from_a_file_without_sidecar_creates_none()
    {
        var path = Photo([1, 2, 3], "image.png");

        Assert.False(UserTags.Change(path, [], ["boulot"]).Changed);
        Assert.Null(UserTags.FindSidecar(path));
    }

    [Fact]
    public void A_sidecar_follows_its_file_to_the_album_trash()
    {
        var path = Photo([1, 2, 3], Path.Combine("2024", "VID_1.mp4"));
        UserTags.Change(path, ["boulot"], []);

        var trashed = Core.Import.AlbumTrash.Move(_temp.Path, path);

        Assert.False(File.Exists(UserTags.SidecarPath(path)));
        Assert.Equal(["boulot"], UserTags.Read(trashed));
    }

    private static byte[] JpegWithPacket(string description)
    {
        var packet = $"""
            <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
             <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            {description}
             </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;
        using var source = new MagickImage(MagickColors.Green, 64, 48);
        source.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes(packet)));
        source.Format = MagickFormat.Jpeg;
        return source.ToByteArray();
    }
}
