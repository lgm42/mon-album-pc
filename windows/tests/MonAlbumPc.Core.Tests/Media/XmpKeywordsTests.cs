using System.Buffers.Binary;
using System.Text;
using ImageMagick;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Xmp;
using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class XmpKeywordsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Adding_a_keyword_keeps_the_image_and_the_other_segments_byte_for_byte()
    {
        var original = PhoneJpeg();

        Assert.Equal(KeywordOutcome.Added, XmpKeywords.Add(original, "WhatsApp", out var tagged));

        var before = Segments(original);
        var after = Segments(tagged);
        Assert.Equal(before.ImageData, after.ImageData); // entropy-coded data, from the start of scan to the end
        Assert.Equal(before.Headers.Select(Convert.ToHexString), after.Headers.Where(s => !IsXmp(s)).Select(Convert.ToHexString));
        Assert.Single(after.Headers, IsXmp);
        // Right after the EXIF segment, where readers look for it.
        Assert.Equal(after.Headers.FindIndex(IsExif) + 1, after.Headers.FindIndex(IsXmp));

        var path = _temp.WriteFile("IMG-20231126-WA0007.jpg", tagged);
        var directories = ImageMetadataReader.ReadMetadata(path);
        Assert.Equal("2024:05:12 10:30:15",
            directories.OfType<ExifSubIfdDirectory>().Single().GetDescription(ExifDirectoryBase.TagDateTimeOriginal));
        Assert.Equal("WhatsApp", directories.OfType<XmpDirectory>().Single().GetXmpProperties()["dc:subject[1]"]);
        Assert.Equal(["WhatsApp"], XmpKeywords.Read(path));
        using var image = new MagickImage(path);
        Assert.NotNull(image.GetColorProfile());
    }

    [Fact]
    public void Adding_the_same_keyword_twice_changes_nothing()
    {
        XmpKeywords.Add(PhoneJpeg(), "Capture d'écran", out var once);

        Assert.Equal(KeywordOutcome.AlreadyThere, XmpKeywords.Add(once, "capture d'écran", out var twice));
        Assert.Same(once, twice);
    }

    [Fact]
    public void A_keyword_joins_those_already_in_the_xmp_packet()
    {
        var packet = """
            <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
             <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
              <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:xmp="http://ns.adobe.com/xap/1.0/" xmp:Rating="3">
               <dc:subject><rdf:Bag><rdf:li>Vacances</rdf:li></rdf:Bag></dc:subject>
              </rdf:Description>
             </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;
        using var source = new MagickImage(MagickColors.Green, 64, 48);
        source.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes(packet)));
        source.Format = MagickFormat.Jpeg;
        var original = source.ToByteArray();

        Assert.Equal(KeywordOutcome.Added, XmpKeywords.Add(original, "Telegram", out var tagged));

        var path = _temp.WriteFile("a.jpg", tagged);
        Assert.Equal(["Vacances", "Telegram"], XmpKeywords.Read(path));
        var properties = ImageMetadataReader.ReadMetadata(path).OfType<XmpDirectory>().Single().GetXmpProperties();
        Assert.Equal("3", properties["xmp:Rating"]);
        Assert.Single(Segments(tagged).Headers, IsXmp);
        Assert.Equal(Segments(original).ImageData, Segments(tagged).ImageData);
    }

    [Fact]
    public void Other_formats_are_left_alone()
    {
        using var image = new MagickImage(MagickColors.Red, 8, 8) { Format = MagickFormat.Png };
        var png = image.ToByteArray();

        Assert.Equal(KeywordOutcome.Unsupported, XmpKeywords.Add(png, "WhatsApp", out var result));
        Assert.Same(png, result);
        Assert.Equal(KeywordOutcome.Unsupported, XmpKeywords.Add([0xFF, 0xD8, 0xFF, 0xE1, 0x00], "WhatsApp", out _));
    }

    [Fact]
    public void Tagging_a_file_keeps_its_modification_date()
    {
        var path = _temp.WriteFile("a.jpg", PhoneJpeg());
        var modified = new DateTime(2023, 11, 26, 10, 12, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, modified);

        Assert.Equal(KeywordOutcome.Added, XmpKeywords.Add(path, "WhatsApp"));
        Assert.Equal(KeywordOutcome.AlreadyThere, XmpKeywords.Add(path, "WhatsApp"));

        Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
        Assert.Equal(["WhatsApp"], XmpKeywords.Read(path));
        Assert.Single(System.IO.Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void Photos_without_keywords_read_as_none() =>
        Assert.Empty(XmpKeywords.Read(_temp.WriteFile("a.jpg", PhoneJpeg())));

    /// <summary>A JPEG like a phone writes it: EXIF with the shooting date, then an ICC profile.</summary>
    internal static byte[] PhoneJpeg(int seed = 0)
    {
        using var image = new MagickImage(new MagickColor((byte)(seed * 40), 120, 200), 64, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.DateTimeOriginal, "2024:05:12 10:30:15");
        image.SetProfile(exif);
        image.SetProfile(ColorProfiles.SRGB);
        image.Format = MagickFormat.Jpeg;
        return image.ToByteArray();
    }

    private static bool IsExif(byte[] segment) => segment[1] == 0xE1 && segment.AsSpan(4).StartsWith("Exif\0\0"u8);

    private static bool IsXmp(byte[] segment) =>
        segment[1] == 0xE1 && segment.AsSpan(4).StartsWith("http://ns.adobe.com/xap/1.0/\0"u8);

    /// <summary>Segments before the image data (marker included), and the image data from the start of scan on.</summary>
    internal static (List<byte[]> Headers, byte[] ImageData) Segments(byte[] jpeg)
    {
        var headers = new List<byte[]>();
        var pos = 2;
        while (jpeg[pos + 1] != 0xDA)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(pos + 2));
            headers.Add(jpeg[pos..(pos + 2 + length)]);
            pos += 2 + length;
        }
        return (headers, jpeg[pos..]);
    }
}
