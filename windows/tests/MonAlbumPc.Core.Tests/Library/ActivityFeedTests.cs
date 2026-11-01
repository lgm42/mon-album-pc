using System.Text;
using MonAlbumPc.Core.Library;

namespace MonAlbumPc.Core.Tests.Library;

public sealed class ActivityFeedTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly Guid _server = Guid.NewGuid();
    private readonly Dictionary<string, long> _positions = [];

    public void Dispose() => _temp.Dispose();

    private static ActivityEntry Entry(string device, int photos = 1, bool complete = false, DateTimeOffset? at = null) => new(
        Guid.NewGuid().ToString(), at ?? DateTimeOffset.Now, Guid.NewGuid(), "Nicolas", device, photos, 0, complete, null, null);

    private void AppendRaw(string text) =>
        File.AppendAllBytes(ActivityFeed.FilePath(_temp.Path, _server), Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Only_new_lines_are_returned()
    {
        ActivityFeed.Append(_temp.Path, _server, Entry("A", 3, complete: true));
        ActivityFeed.Append(_temp.Path, _server, Entry("B"));

        var first = ActivityFeed.ReadNew(_temp.Path, _positions);
        Assert.Equal(["A", "B"], first.Select(e => e.Device));
        Assert.Equal(3, first[0].Photos);
        Assert.True(first[0].Complete);

        Assert.Empty(ActivityFeed.ReadNew(_temp.Path, _positions));

        ActivityFeed.Append(_temp.Path, _server, Entry("C"));
        Assert.Equal(["C"], ActivityFeed.ReadNew(_temp.Path, _positions).Select(e => e.Device));
    }

    [Fact]
    public void Positions_survive_a_copy_like_a_restart_of_the_app()
    {
        ActivityFeed.Append(_temp.Path, _server, Entry("A"));
        ActivityFeed.ReadNew(_temp.Path, _positions);
        var saved = new Dictionary<string, long>(_positions);

        ActivityFeed.Append(_temp.Path, _server, Entry("B"));

        Assert.Equal(["B"], ActivityFeed.ReadNew(_temp.Path, saved).Select(e => e.Device));
    }

    [Fact]
    public void A_line_still_being_written_is_read_once_finished()
    {
        ActivityFeed.Append(_temp.Path, _server, Entry("A"));
        var line = System.Text.Json.JsonSerializer.Serialize(Entry("Téléphone é"), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        // Cut in the middle of a multi-byte character, as a slow write over the network could leave it.
        var bytes = Encoding.UTF8.GetBytes(line);
        var cut = Array.IndexOf(bytes, (byte)0xC3) + 1;
        var path = ActivityFeed.FilePath(_temp.Path, _server);
        File.AppendAllBytes(path, bytes[..cut]);

        Assert.Equal(["A"], ActivityFeed.ReadNew(_temp.Path, _positions).Select(e => e.Device));
        Assert.Empty(ActivityFeed.ReadNew(_temp.Path, _positions));

        File.AppendAllBytes(path, [.. bytes[cut..], (byte)'\n']);

        Assert.Equal(["Téléphone é"], ActivityFeed.ReadNew(_temp.Path, _positions).Select(e => e.Device));
    }

    [Fact]
    public void A_damaged_line_does_not_hide_the_others()
    {
        ActivityFeed.Append(_temp.Path, _server, Entry("A"));
        AppendRaw("{ damaged\n");
        ActivityFeed.Append(_temp.Path, _server, Entry("B"));

        Assert.Equal(["A", "B"], ActivityFeed.ReadNew(_temp.Path, _positions).Select(e => e.Device));
        Assert.Empty(ActivityFeed.ReadNew(_temp.Path, _positions));
    }

    [Fact]
    public void Each_server_has_its_own_position_and_lines_come_in_time_order()
    {
        var now = DateTimeOffset.Now;
        var other = Guid.NewGuid();
        ActivityFeed.Append(_temp.Path, _server, Entry("Second", at: now));
        ActivityFeed.Append(_temp.Path, other, Entry("First", at: now.AddMinutes(-5)));

        Assert.Equal(["First", "Second"], ActivityFeed.ReadNew(_temp.Path, _positions).Select(e => e.Device));
        Assert.Equal(2, _positions.Count);
    }

    [Fact]
    public void A_shorter_file_was_replaced_and_is_read_from_its_beginning()
    {
        ActivityFeed.Append(_temp.Path, _server, Entry("A"));
        ActivityFeed.Append(_temp.Path, _server, Entry("B"));
        ActivityFeed.ReadNew(_temp.Path, _positions);

        File.Delete(ActivityFeed.FilePath(_temp.Path, _server));
        ActivityFeed.Append(_temp.Path, _server, Entry("C"));

        Assert.Equal(["C"], ActivityFeed.ReadNew(_temp.Path, _positions).Select(e => e.Device));
    }

    [Fact]
    public void Skipping_to_the_end_ignores_the_past_but_not_what_follows()
    {
        ActivityFeed.Append(_temp.Path, _server, Entry("Passé"));

        ActivityFeed.SkipToEnd(_temp.Path, _positions);

        Assert.Empty(ActivityFeed.ReadNew(_temp.Path, _positions));
        ActivityFeed.Append(_temp.Path, _server, Entry("Nouveau"));
        Assert.Equal(["Nouveau"], ActivityFeed.ReadNew(_temp.Path, _positions).Select(e => e.Device));
    }

    [Fact]
    public void An_unreachable_feed_returns_nothing_and_keeps_the_positions()
    {
        _positions["x.jsonl"] = 42;

        Assert.Empty(ActivityFeed.ReadNew(_temp.Combine("absent"), _positions));
        Assert.Equal(42, _positions["x.jsonl"]);
    }
}
