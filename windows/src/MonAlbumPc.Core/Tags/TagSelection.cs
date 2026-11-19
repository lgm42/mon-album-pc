using MonAlbumPc.Core.Activity;
using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Core.Tags;

/// <summary>A line of the tag filter: a tag, shown by its last name under its parent, and its photos in view.</summary>
public sealed record TagFilterEntry(string Name, int Count)
{
    public string Leaf => UserTags.Leaf(Name);

    public int Depth => Name.Count(c => c == UserTags.Separator);
}

/// <summary>
/// The tags chosen in the filter: photos carrying any of them (a parent takes its children along), and those without tag
/// when <see cref="Untagged"/>. Nothing chosen: every photo.
/// </summary>
public sealed class TagSelection
{
    public HashSet<string> Tags { get; } = new(UserTags.Same);

    public bool Untagged { get; set; }

    public bool IsEmpty => Tags.Count == 0 && !Untagged;

    public bool Matches(IReadOnlyList<string> tags) =>
        IsEmpty || (Untagged && tags.Count == 0) || tags.Any(t => Tags.Any(chosen => UserTags.IsWithin(t, chosen)));

    public void Toggle(string tag)
    {
        if (!Tags.Remove(tag))
        {
            Tags.Add(tag);
        }
    }

    public void Clear()
    {
        Tags.Clear();
        Untagged = false;
    }

    /// <summary>Forgets the tags that no longer exist (renamed or deleted).</summary>
    public void Retain(IEnumerable<string> known)
    {
        var names = new HashSet<string>(known, UserTags.Same);
        Tags.RemoveWhere(t => !names.Contains(t));
    }

    /// <summary>"boulot", "boulot, Sans tag", "3 tags"; empty when nothing is chosen.</summary>
    public string Describe()
    {
        List<string> parts = [.. Tags.Order(TagCatalog.TagOrder.Instance)];
        if (Untagged)
        {
            parts.Add("Sans tag");
        }
        return parts.Count <= 2 ? string.Join(", ", parts) : WorkText.Plural(parts.Count, "tag", "tags");
    }

    /// <summary>
    /// Every tag of <paramref name="known"/> and the parents of those (to draw the tree), with the number of
    /// <paramref name="items"/> carrying it or a tag under it.
    /// </summary>
    public static List<TagFilterEntry> Entries(IEnumerable<string> known, IEnumerable<IReadOnlyList<string>> items)
    {
        var counts = new Dictionary<string, int>(UserTags.Same);
        foreach (var name in known.SelectMany(SelfAndParents))
        {
            counts.TryAdd(name, 0);
        }
        foreach (var tags in items)
        {
            if (tags.Count == 0)
            {
                continue;
            }
            foreach (var name in tags.SelectMany(SelfAndParents).Distinct(UserTags.Same))
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }
        return [.. counts.Select(c => new TagFilterEntry(c.Key, c.Value)).OrderBy(e => e.Name, TagCatalog.TagOrder.Instance)];
    }

    private static IEnumerable<string> SelfAndParents(string tag)
    {
        for (string? name = tag; name is not null; name = UserTags.Parent(name))
        {
            yield return name;
        }
    }
}
