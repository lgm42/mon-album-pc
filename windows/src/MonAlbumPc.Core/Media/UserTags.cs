using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MonAlbumPc.Core.Media;

/// <summary>What changing the tags of a file did: <see cref="Tags"/> are its user tags afterwards.</summary>
public sealed record TagWrite(bool Changed, IReadOnlyList<string> Tags);

/// <summary>
/// User tags ("boulot", "Bricolage/ESP32") kept in the files as photo tools do: each tag in <c>lr:hierarchicalSubject</c>
/// ("Bricolage|ESP32", read by Lightroom and digiKam), its name and its parents' in <c>dc:subject</c> (the flat keywords of
/// Windows, Immich, Lightroom), and in <c>digiKam:TagsList</c> when the packet already has one. JPEG photos carry them in
/// their own XMP segment, rewritten alone (<see cref="XmpKeywords"/>); videos and other images in a sidecar
/// <c>name.ext.xmp</c> (digiKam's default, darktable), or <c>name.xmp</c> (Lightroom's form) when that one is already there.
/// A keyword found only in <c>dc:subject</c> is a tag too (added by Windows or another tool), except the provenance keyword
/// of the file: the provenance filter shows that one.
/// </summary>
public static class UserTags
{
    public const char Separator = '/';
    public const int MaxLength = 120;

    public static readonly StringComparer Same = StringComparer.OrdinalIgnoreCase;

    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Lr = "http://ns.adobe.com/lightroom/1.0/";
    private static readonly XNamespace DigiKam = "http://www.digikam.org/ns/1.0/";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>A tag as typed, tidied: " Bricolage / ESP32 " → "Bricolage/ESP32" ('|' and '\' separate too); null when empty.</summary>
    public static string? Normalize(string? text)
    {
        if (text is null)
        {
            return null;
        }
        var segments = text
            .Split([Separator, '|', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        var tag = string.Join(Separator, segments);
        if (tag.Length > MaxLength)
        {
            tag = tag[..MaxLength].TrimEnd(Separator, ' ');
        }
        return tag.Length == 0 ? null : tag;
    }

    /// <summary>"Bricolage/ESP32" → "ESP32".</summary>
    public static string Leaf(string tag) => tag[(tag.LastIndexOf(Separator) + 1)..];

    /// <summary>"Bricolage/ESP32" → "Bricolage"; null for a top-level tag.</summary>
    public static string? Parent(string tag) => tag.LastIndexOf(Separator) is var i and > 0 ? tag[..i] : null;

    /// <summary>True for <paramref name="ancestor"/> itself and the tags under it ("Bricolage/ESP32" is within "Bricolage").</summary>
    public static bool IsWithin(string tag, string ancestor) =>
        Same.Equals(tag, ancestor) || tag.StartsWith(ancestor + Separator, StringComparison.OrdinalIgnoreCase);

    /// <summary>Photos whose tags go into the file itself; the others get a sidecar.</summary>
    public static bool IsEmbedded(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg";

    /// <summary>The sidecar written for a file that has none yet: "VID_1.mp4" → "VID_1.mp4.xmp".</summary>
    public static string SidecarPath(string mediaPath) => mediaPath + ".xmp";

    /// <summary>The sidecar of a file: "name.ext.xmp", else "name.xmp" (looked up in this order, as digiKam does); null when none.</summary>
    public static string? FindSidecar(string mediaPath)
    {
        var full = SidecarPath(mediaPath);
        if (File.Exists(full))
        {
            return full;
        }
        var shortName = Path.ChangeExtension(mediaPath, ".xmp");
        return File.Exists(shortName) ? shortName : null;
    }

    /// <summary>The user tags of a file (XMP of a JPEG and sidecar), without <paramref name="provenanceKeyword"/>.</summary>
    public static List<string> Read(string path, string? provenanceKeyword = null)
    {
        var tags = new List<string>();
        if (IsEmbedded(path) && XmpKeywords.ReadPacket(path) is { } packet)
        {
            Merge(tags, FromPacket(packet, provenanceKeyword));
        }
        if (FindSidecar(path) is { } sidecar && LoadSidecar(sidecar) is { } side)
        {
            Merge(tags, FromPacket(side, provenanceKeyword));
        }
        return tags;
    }

    /// <summary>The user tags of an XMP packet, without <paramref name="provenanceKeyword"/>.</summary>
    public static List<string> FromPacket(XDocument packet, string? provenanceKeyword = null)
    {
        var tags = new List<string>();
        Merge(tags, Items(packet, Lr + "hierarchicalSubject").Concat(Items(packet, DigiKam + "TagsList")).Select(Normalize).OfType<string>());
        // Parents and leaves of the hierarchical tags are in dc:subject as well: not tags of their own.
        var covered = new HashSet<string>(tags.SelectMany(t => t.Split(Separator)), Same);
        Merge(tags, Items(packet, Dc + "subject").Select(Normalize).OfType<string>()
            .Where(k => !covered.Contains(k) && !Same.Equals(k, provenanceKeyword)));
        return tags;
    }

    /// <summary>
    /// Adds and removes tags in a file, without re-encoding it and keeping its date: in the XMP segment of a JPEG, and in
    /// its sidecar when it has one (or cannot carry them itself). <paramref name="keep"/>, the provenance keyword, stays in
    /// <c>dc:subject</c> whatever is removed.
    /// </summary>
    /// <exception cref="InvalidDataException">The XMP of the file or its sidecar cannot be read or updated safely.</exception>
    public static TagWrite Change(string path, IEnumerable<string> add, IEnumerable<string> remove, string? keep = null)
    {
        var adding = add.Select(Normalize).OfType<string>().Distinct(Same).ToList();
        var removing = remove.Select(Normalize).OfType<string>().Where(r => !adding.Contains(r, Same)).Distinct(Same).ToList();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Fichier supprimé ou déplacé entre-temps.", path);
        }
        var changed = false;
        var embedded = false;
        if (IsEmbedded(path))
        {
            var inner = XmpUpdate.Unchanged;
            var update = XmpKeywords.Update(File.ReadAllBytes(path), packet => (inner = Change(packet, adding, removing, keep)) == XmpUpdate.Changed,
                out var updated);
            if (update == XmpUpdate.Changed)
            {
                XmpKeywords.Replace(path, updated);
                changed = true;
            }
            embedded = update != XmpUpdate.Unsupported && inner != XmpUpdate.Unsupported;
        }
        var sidecar = FindSidecar(path);
        if (sidecar is not null || (!embedded && adding.Count > 0))
        {
            changed |= ChangeSidecar(sidecar ?? SidecarPath(path), adding, removing, keep);
        }
        return new TagWrite(changed, Read(path, keep));
    }

    /// <summary>Adds and removes tags in an XMP packet.</summary>
    public static XmpUpdate Change(XDocument packet, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, string? keep = null)
    {
        if (packet.Descendants(Rdf + "RDF").FirstOrDefault() is not { } rdf)
        {
            return XmpUpdate.Unsupported;
        }
        var removing = new HashSet<string>(remove, Same);
        var after = FromPacket(packet).Where(t => !removing.Contains(t)).ToList();
        Merge(after, add);
        var needed = new HashSet<string>(after.SelectMany(t => t.Split(Separator)), Same);
        var dropped = new HashSet<string>(removing.SelectMany(t => t.Split(Separator)).Where(s => !needed.Contains(s) && !Same.Equals(s, keep)), Same);

        if (!TryBag(rdf, Lr + "hierarchicalSubject", "lr", add.Count > 0, out var hierarchical)
            || !TryBag(rdf, DigiKam + "TagsList", "digiKam", create: false, out var digiKam)
            || !TryBag(rdf, Dc + "subject", "dc", add.Count > 0, out var subject))
        {
            return XmpUpdate.Unsupported;
        }
        var changed = false;
        changed |= Edit(hierarchical, li => removing.Contains(Normalize(li) ?? ""), add.Select(a => a.Replace(Separator, '|')));
        changed |= Edit(digiKam, li => removing.Contains(Normalize(li) ?? ""), add);
        changed |= Edit(subject, li => dropped.Contains(Normalize(li) ?? ""), add.SelectMany(a => a.Split(Separator).Reverse()));
        return changed ? XmpUpdate.Changed : XmpUpdate.Unchanged;
    }

    private static bool ChangeSidecar(string sidecar, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, string? keep)
    {
        var exists = File.Exists(sidecar);
        var packet = exists
            ? LoadSidecar(sidecar) ?? throw new InvalidDataException($"Fichier XMP illisible : {Path.GetFileName(sidecar)}")
            : XDocument.Parse(XmpKeywords.EmptyPacket, LoadOptions.PreserveWhitespace);
        switch (Change(packet, add, remove, keep))
        {
            case XmpUpdate.Unsupported:
                throw new InvalidDataException($"Fichier XMP inhabituel, laissé tel quel : {Path.GetFileName(sidecar)}");
            case XmpUpdate.Unchanged:
                return false;
        }
        var temp = sidecar + ".tmp";
        try
        {
            File.WriteAllText(temp, (packet.Declaration is { } declaration ? declaration + "\n" : "") + packet.ToString(SaveOptions.DisableFormatting), Utf8);
            File.Move(temp, sidecar, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
        return true;
    }

    private static XDocument? LoadSidecar(string sidecar)
    {
        try
        {
            return XDocument.Parse(File.ReadAllText(sidecar, Encoding.UTF8), LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Removes the items matching <paramref name="drop"/> and adds those of <paramref name="add"/> not there yet.</summary>
    private static bool Edit(XElement? bag, Func<string, bool> drop, IEnumerable<string> add)
    {
        if (bag is null)
        {
            return false;
        }
        var changed = false;
        foreach (var li in bag.Elements(Rdf + "li").Where(li => drop(li.Value.Trim())).ToList())
        {
            li.Remove();
            changed = true;
        }
        foreach (var item in add)
        {
            if (!bag.Elements(Rdf + "li").Any(li => Same.Equals(li.Value.Trim(), item)))
            {
                bag.Add(new XElement(Rdf + "li", item));
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// The bag (or sequence) of a list property, made when <paramref name="create"/> and missing; null when missing. False
    /// when the property is not in the standard form: then the packet is left alone.
    /// </summary>
    private static bool TryBag(XElement rdf, XName property, string prefix, bool create, out XElement? bag)
    {
        bag = null;
        var element = rdf.Elements(Rdf + "Description").Elements(property).FirstOrDefault();
        if (element is null)
        {
            if (!create)
            {
                return true;
            }
            var description = rdf.Element(Rdf + "Description");
            if (description is null)
            {
                description = new XElement(Rdf + "Description", new XAttribute(Rdf + "about", ""));
                rdf.Add(description);
            }
            if (description.GetPrefixOfNamespace(property.Namespace) is null)
            {
                description.Add(new XAttribute(XNamespace.Xmlns + prefix, property.Namespace));
            }
            element = new XElement(property);
            description.Add(element);
        }
        bag = element.Element(Rdf + "Bag") ?? element.Element(Rdf + "Seq");
        if (bag is null)
        {
            if (element.HasElements || !string.IsNullOrWhiteSpace(element.Value))
            {
                return false;
            }
            if (!create)
            {
                return true;
            }
            bag = new XElement(Rdf + "Bag");
            element.Add(bag);
        }
        return true;
    }

    private static IEnumerable<string> Items(XDocument packet, XName property) =>
        packet.Descendants(Rdf + "Description").Elements(property).Elements()
            .Where(e => e.Name == Rdf + "Bag" || e.Name == Rdf + "Seq")
            .Elements(Rdf + "li").Select(li => li.Value.Trim()).Where(v => v.Length > 0);

    private static void Merge(List<string> tags, IEnumerable<string> more)
    {
        foreach (var tag in more)
        {
            if (!tags.Contains(tag, Same))
            {
                tags.Add(tag);
            }
        }
    }
}
