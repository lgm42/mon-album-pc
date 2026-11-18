using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class SelectionTests
{
    private static readonly string[] Order = ["a", "b", "c", "d", "e", "f"];
    private readonly Selection<string> _selection = new();

    private string Selected => string.Concat(Order.Where(_selection.Contains));

    [Fact]
    public void Click_selects_only_the_clicked_item()
    {
        _selection.Click(Order, "b", toggle: false, range: false);
        _selection.Click(Order, "d", toggle: false, range: false);

        Assert.Equal("d", Selected);
        Assert.Equal("d", _selection.Current);
    }

    [Fact]
    public void Ctrl_click_adds_then_removes_an_item()
    {
        _selection.Click(Order, "b", toggle: false, range: false);
        _selection.Click(Order, "d", toggle: true, range: false);
        Assert.Equal("bd", Selected);

        _selection.Click(Order, "b", toggle: true, range: false);
        Assert.Equal("d", Selected);
        Assert.Equal("b", _selection.Current);
    }

    [Fact]
    public void Shift_click_selects_from_the_anchor_in_both_directions()
    {
        _selection.Click(Order, "c", toggle: false, range: false);
        _selection.Click(Order, "e", toggle: false, range: true);
        Assert.Equal("cde", Selected);

        // The anchor stays: the range is recomputed from it, not added.
        _selection.Click(Order, "a", toggle: false, range: true);
        Assert.Equal("abc", Selected);
        Assert.Equal("c", _selection.Anchor);
        Assert.Equal("a", _selection.Current);
    }

    [Fact]
    public void Ctrl_shift_click_adds_the_range_to_the_selection()
    {
        _selection.Click(Order, "a", toggle: false, range: false);
        _selection.Click(Order, "d", toggle: true, range: false);
        _selection.Click(Order, "f", toggle: true, range: true);

        Assert.Equal("adef", Selected);
    }

    [Fact]
    public void Shift_click_without_anchor_selects_the_item()
    {
        _selection.Click(Order, "c", toggle: false, range: true);

        Assert.Equal("c", Selected);
        Assert.Equal("c", _selection.Anchor);
    }

    [Fact]
    public void Select_all_keeps_the_focus()
    {
        _selection.Click(Order, "c", toggle: false, range: false);
        _selection.SelectAll(Order);

        Assert.Equal("abcdef", Selected);
        Assert.Equal("c", _selection.Current);
    }

    [Fact]
    public void Hidden_items_leave_the_selection()
    {
        _selection.Click(Order, "b", toggle: false, range: false);
        _selection.Click(Order, "e", toggle: false, range: true);

        _selection.Retain(i => i is not "b" and not "c");

        Assert.Equal("de", Selected);
        Assert.Null(_selection.Anchor);
        Assert.Equal("e", _selection.Current);
    }

    [Fact]
    public void Range_after_the_anchor_is_hidden_selects_the_item()
    {
        _selection.Click(Order, "b", toggle: false, range: false);
        _selection.Retain(i => i != "b");

        _selection.Click(Order, "d", toggle: false, range: true);

        Assert.Equal("d", Selected);
    }
}
