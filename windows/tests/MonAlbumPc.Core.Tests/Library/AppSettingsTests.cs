using MonAlbumPc.Core.Library;

namespace MonAlbumPc.Core.Tests.Library;

public sealed class AppSettingsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Without_settings_the_app_is_autonomous() => Assert.Equal(AppMode.Autonomous, AppSettings.Load(_temp.Path).Mode);

    [Fact]
    public void Viewer_mode_round_trips_with_the_documented_format()
    {
        new AppSettings(AppMode.Viewer, @"\\nas\donnees\Photos").Save(_temp.Path);

        var read = AppSettings.Load(_temp.Path);
        Assert.True(read.IsViewer);
        Assert.Equal(@"\\nas\donnees\Photos", read.LibraryRoot);
        var json = File.ReadAllText(AppSettings.FilePath(_temp.Path));
        Assert.Contains("\"mode\": \"viewer\"", json);
        Assert.Contains("\"libraryRoot\"", json);
    }

    [Theory]
    [InlineData("{ cut")]
    [InlineData("{ \"mode\": \"inconnu\" }")]
    [InlineData("{ \"mode\": \"viewer\" }")]
    [InlineData("{ \"mode\": \"viewer\", \"libraryRoot\": \"\" }")]
    public void Unusable_settings_fall_back_on_the_autonomous_mode(string content)
    {
        File.WriteAllText(AppSettings.FilePath(_temp.Path), content);

        Assert.False(AppSettings.Load(_temp.Path).IsViewer);
    }

    [Fact]
    public void The_viewer_never_touches_the_autonomous_catalog()
    {
        Assert.Equal(_temp.Path, new AppSettings().CatalogDirectory(_temp.Path));
        Assert.Equal(_temp.Combine("viewer"), new AppSettings(AppMode.Viewer, "x").CatalogDirectory(_temp.Path));
    }
}
