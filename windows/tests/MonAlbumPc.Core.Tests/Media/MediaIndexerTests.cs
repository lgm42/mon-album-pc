using Microsoft.Data.Sqlite;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class MediaIndexerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_catalog_out_of_reach_ends_the_pass_instead_of_the_process()
    {
        var folder = _temp.Combine("catalog");
        using var core = CoreServices.Create(folder);
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
        using var finished = new ManualResetEventSlim();
        bool? changed = null;
        core.MediaIndexer.Completed += c =>
        {
            changed = c;
            finished.Set();
        };

        core.MediaIndexer.RequestRefresh();

        // Generous: the indexing thread has a low priority, and gets little time on a busy machine.
        Assert.True(finished.Wait(TimeSpan.FromSeconds(60)));
        Assert.False(changed);
    }
}
