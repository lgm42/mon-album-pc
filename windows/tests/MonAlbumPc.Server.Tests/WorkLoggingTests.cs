using Microsoft.Extensions.Logging;
using MonAlbumPc.Core.Activity;

namespace MonAlbumPc.Server.Tests;

public sealed class WorkLoggingTests : IDisposable
{
    private readonly WorkBoard _board = new(TimeProvider.System);
    private readonly RecordingLoggerProvider _file = new();
    private readonly WorkBoardLoggerProvider _logs;

    public WorkLoggingTests() => _logs = new WorkBoardLoggerProvider(_board, _file);

    public void Dispose()
    {
        _logs.Dispose();
        _board.Dispose();
    }

    [Fact]
    public void Warnings_and_errors_of_the_logs_show_on_the_board_and_still_reach_the_file()
    {
        _logs.CreateLogger("MonAlbumPc.Nas.ThumbnailKeeper").LogWarning("ffmpeg introuvable : les vidéos n'auront pas de miniature");
        _logs.CreateLogger("MonAlbumPc.Nas.NasHost").LogError(new IOException("Address already in use"), "Le serveur des téléphones n'a pas pu démarrer");
        _logs.CreateLogger("MonAlbumPc.Server.Api").LogInformation("Fichier reçu");

        Assert.Equal(
            [
                (WorkLevel.Error, "Le serveur des téléphones n'a pas pu démarrer (Address already in use)"),
                (WorkLevel.Warning, "ffmpeg introuvable : les vidéos n'auront pas de miniature"),
            ],
            _board.Events.Select(e => (e.Level, e.Text)));
        Assert.Equal(3, _file.Lines.Count);
    }

    [Fact]
    public void Framework_warnings_stay_in_the_logs()
    {
        _logs.CreateLogger("Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager").LogWarning("No XML encryptor configured.");

        Assert.Empty(_board.Events);
        Assert.Single(_file.Lines);
    }

    [Fact]
    public void Tasks_get_one_line_at_their_start_and_one_at_their_end()
    {
        using var log = new WorkLog(_board, _logs.CreateLogger(WorkLog.Category));

        var job = _board.Start("Miniatures", "Inventaire de Famille");
        for (var i = 0; i < 50; i++)
        {
            job.Advance($"IMG_{i}.jpg");
        }
        job.Finish("Miniatures : 50 créées en 1 s");
        _board.Start("Bibliothèque", silent: true).Dispose();

        Assert.Equal(
            [
                (LogLevel.Information, "Miniatures : début (Inventaire de Famille)"),
                (LogLevel.Information, "Miniatures : 50 créées en 1 s"),
            ],
            _file.Lines.Select(l => (l.Level, l.Message)));
        // Its own lines are not taken back from the logs.
        Assert.Single(_board.Events);
    }

    [Fact]
    public void Events_told_by_the_app_are_logged_once()
    {
        using var log = new WorkLog(_board, _logs.CreateLogger(WorkLog.Category));

        _board.Warn("Bibliothèque injoignable : \\\\nas\\Telephones");

        Assert.Equal((LogLevel.Warning, "Bibliothèque injoignable : \\\\nas\\Telephones"), _file.Lines.Select(l => (l.Level, l.Message)).Single());
        Assert.Single(_board.Events);
    }
}
