using System.Buffers.Binary;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MonAlbumPc.Core.Media;

public enum KeywordOutcome { Added, AlreadyThere, Unsupported }

public enum XmpUpdate { Changed, Unchanged, Unsupported }

/// <summary>
/// Keywords of the XMP packet of JPEG photos (<c>dc:subject</c>: « Mots clés » in Windows, keywords in Lightroom, digiKam,
/// Immich). Adding one only inserts or rewrites the XMP segment: the image is never re-encoded and every other segment
/// (EXIF, ICC profile…) stays byte for byte.
/// </summary>
public static class XmpKeywords
{
    private static readonly byte[] XmpHeader = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Segment length field (2 bytes) included: a JPEG segment holds at most 65 535 bytes.
    private const int MaxSegmentLength = ushort.MaxValue;

    internal const string EmptyPacket =
        "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" +
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        "<rdf:Description rdf:about=\"\"/></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";

    /// <summary>Keywords of a JPEG, reading only the segments before the image data. Empty when none or not a JPEG.</summary>
    public static List<string> Read(string path) => ReadPacket(path) is { } packet ? Keywords(packet) : [];

    /// <summary>The XMP packet of a JPEG, reading only the segments before the image data. Null when none or not a JPEG.</summary>
    public static XDocument? ReadPacket(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
        Span<byte> head = stackalloc byte[4];
        if (stream.ReadAtLeast(head[..2], 2, throwOnEndOfStream: false) < 2 || head[0] != 0xFF || head[1] != 0xD8)
        {
            return null;
        }
        while (stream.ReadAtLeast(head, 4, throwOnEndOfStream: false) == 4 && head[0] == 0xFF)
        {
            var marker = head[1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(head[2..]);
            if (marker is 0xDA or 0xD9 || length < 2)
            {
                break;
            }
            if (marker == 0xE1 && length - 2 > XmpHeader.Length)
            {
                var payload = new byte[length - 2];
                if (stream.ReadAtLeast(payload, payload.Length, throwOnEndOfStream: false) < payload.Length)
                {
                    break;
                }
                if (payload.AsSpan().StartsWith(XmpHeader))
                {
                    return Parse(payload.AsSpan(XmpHeader.Length));
                }
                continue;
            }
            stream.Seek(length - 2, SeekOrigin.Current);
        }
        return null;
    }

    /// <summary>
    /// Adds <paramref name="keyword"/> to a JPEG held in memory. <paramref name="tagged"/> is the new file when
    /// <see cref="KeywordOutcome.Added"/>, otherwise the bytes are left as they are.
    /// </summary>
    public static KeywordOutcome Add(byte[] jpeg, string keyword, out byte[] tagged)
    {
        var outcome = KeywordOutcome.AlreadyThere;
        var update = Update(jpeg, packet =>
        {
            outcome = Insert(packet, keyword);
            return outcome == KeywordOutcome.Added;
        }, out tagged);
        return update == XmpUpdate.Unsupported ? KeywordOutcome.Unsupported : outcome;
    }

    /// <summary>Adds <paramref name="keyword"/> to a JPEG file in place, keeping its modification date.</summary>
    public static KeywordOutcome Add(string path, string keyword)
    {
        var outcome = Add(File.ReadAllBytes(path), keyword, out var tagged);
        if (outcome == KeywordOutcome.Added)
        {
            Replace(path, tagged);
        }
        return outcome;
    }

    /// <summary>
    /// Lets <paramref name="change"/> edit the XMP packet of a JPEG held in memory (an empty one when there is none); it
    /// returns whether it changed anything. Only the XMP segment is rewritten: <paramref name="updated"/> is the new file
    /// when <see cref="XmpUpdate.Changed"/>, otherwise the bytes are left as they are.
    /// </summary>
    public static XmpUpdate Update(byte[] jpeg, Func<XDocument, bool> change, out byte[] updated)
    {
        updated = jpeg;
        if (Locate(jpeg) is not { } layout)
        {
            return XmpUpdate.Unsupported;
        }
        var packet = layout.Xmp is { } xmp
            ? Parse(jpeg.AsSpan(xmp.Start + 4 + XmpHeader.Length, xmp.Length - 4 - XmpHeader.Length))
            : XDocument.Parse(EmptyPacket, LoadOptions.PreserveWhitespace);
        if (packet is null)
        {
            return XmpUpdate.Unsupported;
        }
        if (!change(packet))
        {
            return XmpUpdate.Unchanged;
        }

        var text = Utf8.GetBytes(packet.ToString(SaveOptions.DisableFormatting));
        var segmentLength = 2 + XmpHeader.Length + text.Length;
        if (segmentLength > MaxSegmentLength)
        {
            return XmpUpdate.Unsupported; // would need extended XMP: not worth it for keywords
        }
        var segment = new byte[2 + segmentLength];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)segmentLength);
        XmpHeader.CopyTo(segment, 4);
        text.CopyTo(segment, 4 + XmpHeader.Length);

        var (start, removed) = layout.Xmp is { } old ? (old.Start, old.Length) : (layout.InsertAt, 0);
        updated = [.. jpeg.AsSpan(0, start), .. segment, .. jpeg.AsSpan(start + removed)];
        return XmpUpdate.Changed;
    }

    /// <summary>Replaces a file with <paramref name="content"/> at once (temporary file, then moved), keeping its date.</summary>
    internal static void Replace(string path, byte[] content)
    {
        var temp = path + ".tagging";
        var modified = File.GetLastWriteTimeUtc(path);
        try
        {
            File.WriteAllBytes(temp, content);
            File.SetLastWriteTimeUtc(temp, modified);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static KeywordOutcome Insert(XDocument packet, string keyword)
    {
        if (packet.Descendants(Rdf + "RDF").FirstOrDefault() is not { } rdf)
        {
            return KeywordOutcome.Unsupported;
        }
        var subject = rdf.Elements(Rdf + "Description").Elements(Dc + "subject").FirstOrDefault();
        if (subject is null)
        {
            var description = rdf.Element(Rdf + "Description");
            if (description is null)
            {
                description = new XElement(Rdf + "Description", new XAttribute(Rdf + "about", ""));
                rdf.Add(description);
            }
            if (description.GetPrefixOfNamespace(Dc) is null)
            {
                description.Add(new XAttribute(XNamespace.Xmlns + "dc", Dc));
            }
            subject = new XElement(Dc + "subject");
            description.Add(subject);
        }
        var bag = subject.Element(Rdf + "Bag") ?? subject.Element(Rdf + "Seq");
        if (bag is null)
        {
            if (subject.HasElements || !string.IsNullOrWhiteSpace(subject.Value))
            {
                return KeywordOutcome.Unsupported; // not the standard form: leave it alone
            }
            bag = new XElement(Rdf + "Bag");
            subject.Add(bag);
        }
        if (bag.Elements(Rdf + "li").Any(li => string.Equals(li.Value.Trim(), keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return KeywordOutcome.AlreadyThere;
        }
        bag.Add(new XElement(Rdf + "li", keyword));
        return KeywordOutcome.Added;
    }

    private static List<string> Keywords(XDocument packet) =>
    [
        .. packet.Descendants(Rdf + "Description").Elements(Dc + "subject").Elements()
            .Elements(Rdf + "li").Select(li => li.Value.Trim()).Where(k => k.Length > 0),
    ];

    internal static XDocument? Parse(ReadOnlySpan<byte> packet)
    {
        try
        {
            return XDocument.Parse(Utf8.GetString(packet).TrimEnd('\0'), LoadOptions.PreserveWhitespace);
        }
        catch (Exception e) when (e is XmlException or DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where the XMP segment is (marker included), or where a new one goes: after the leading JFIF/EXIF segments, as
    /// readers expect them first. Null when this is not a well-formed JPEG up to its image data.
    /// </summary>
    private static Layout? Locate(byte[] b)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8)
        {
            return null;
        }
        var pos = 2;
        var insertAt = 2;
        var leading = true;
        Segment? xmp = null;
        while (pos + 4 <= b.Length)
        {
            if (b[pos] != 0xFF)
            {
                return null;
            }
            var marker = b[pos + 1];
            if (marker == 0xFF)
            {
                pos++; // fill byte
                continue;
            }
            if (marker == 0xDA)
            {
                return new Layout(insertAt, xmp);
            }
            if (marker is 0xD9 or 0xD8)
            {
                return null;
            }
            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                pos += 2;
                continue;
            }
            var length = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(pos + 2));
            if (length < 2 || pos + 2 + length > b.Length)
            {
                return null;
            }
            if (marker == 0xE1 && xmp is null && b.AsSpan(pos + 4, length - 2).StartsWith(XmpHeader))
            {
                xmp = new Segment(pos, 2 + length);
            }
            leading &= marker is 0xE0 or 0xE1;
            if (leading)
            {
                insertAt = pos + 2 + length;
            }
            pos += 2 + length;
        }
        return null;
    }

    private readonly record struct Segment(int Start, int Length);

    private readonly record struct Layout(int InsertAt, Segment? Xmp);
}
