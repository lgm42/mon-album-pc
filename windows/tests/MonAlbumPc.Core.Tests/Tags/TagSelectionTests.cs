using MonAlbumPc.Core.Tags;

namespace MonAlbumPc.Core.Tests.Tags;

public sealed class TagSelectionTests
{
    private static readonly IReadOnlyList<string>[] Items =
    [
        ["Bricolage/ESP32"],
        ["Bricolage/Arduino", "boulot"],
        ["Bricolage"],
        [],
    ];

    [Fact]
    public void A_parent_counts_and_matches_its_children()
    {
        var entries = TagSelection.Entries(["boulot", "Bricolage/Arduino", "Bricolage/ESP32", "Vacances"], Items);

        Assert.Equal(["boulot", "Bricolage", "Bricolage/Arduino", "Bricolage/ESP32", "Vacances"], entries.Select(e => e.Name));
        Assert.Equal([1, 3, 1, 1, 0], entries.Select(e => e.Count));
        Assert.Equal([0, 0, 1, 1, 0], entries.Select(e => e.Depth));

        var selection = new TagSelection();
        selection.Toggle("bricolage");
        Assert.Equal(3, Items.Count(selection.Matches));
    }

    [Fact]
    public void Without_tag_and_with_a_tag_add_up()
    {
        var selection = new TagSelection { Untagged = true };
        selection.Toggle("boulot");

        Assert.Equal([false, true, false, true], Items.Select(selection.Matches));
        Assert.Equal("boulot, Sans tag", selection.Describe());
        Assert.True(new TagSelection().Matches([]));
    }
}
