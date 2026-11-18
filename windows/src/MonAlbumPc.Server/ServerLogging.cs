using System.Text;
using Microsoft.Extensions.Logging;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Server;

/// <summary>Hands the logs of the phone server to the logging of its host (console of the NAS container).</summary>
public sealed class ForwardingLoggerProvider(ILoggerFactory target) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => target.CreateLogger(categoryName);

    public void Dispose()
    {
        // The host owns the factory.
    }
}

/// <summary>
/// Logs of the phone server in a small text file (the Windows app has no console): warnings of ASP.NET, everything of ours
/// from <see cref="LogLevel.Information"/>. The file is set aside once it reaches 1 MB, keeping one older file.
/// </summary>
public sealed class FileLoggerProvider(string path) : ILoggerProvider
{
    private const long MaxSize = 1 << 20;
    private readonly Lock _lock = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        // The file is opened for each line.
    }

    private void Write(string line)
    {
        lock (_lock)
        {
            try
            {
                var file = new FileInfo(path);
                if (file.Exists && file.Length > MaxSize)
                {
                    File.Move(path, Path.ChangeExtension(path, ".old.log"), overwrite: true);
                }
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging never stops the server.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        private readonly LogLevel _minimum = category.StartsWith("Microsoft", StringComparison.Ordinal) ? LogLevel.Warning : LogLevel.Information;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimum && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(' ')
                .Append(logLevel.ToString().ToUpperInvariant()).Append(' ')
                .Append(category).Append(": ").Append(formatter(state, exception))
                .AppendLine();
            if (exception is not null)
            {
                line.AppendLine(exception.ToString());
            }
            provider.Write(line.ToString());
        }
    }
}

/// <summary>One line per backup in the logs, at its start and at its end, so that they tell what the phones did.</summary>
internal sealed class SessionLog : IDisposable
{
    private readonly CoreServices _core;
    private readonly ILogger _log;

    public SessionLog(CoreServices core, ILogger log)
    {
        _core = core;
        _log = log;
        core.Sessions.Started += OnStarted;
        core.Sessions.Closed += OnClosed;
    }

    public void Dispose()
    {
        _core.Sessions.Started -= OnStarted;
        _core.Sessions.Closed -= OnClosed;
    }

    private string Phone(Guid deviceId) => _core.Devices.Get(deviceId)?.Name ?? deviceId.ToString();

    private string Album(Guid albumId) => _core.Albums.Get(albumId)?.Name ?? albumId.ToString();

    private void OnStarted(ActiveSession session) =>
        _log.LogInformation("Sauvegarde de {Phone} commencée (album {Album}, {Channel})", Phone(session.DeviceId),
            Album(session.AlbumId), session.Channel);

    private void OnClosed(BackupSession session)
    {
        var how = session switch
        {
            { HasError: true } => $"erreur {session.ErrorCode} : {session.ErrorMessage}",
            { Complete: true } => "téléphone à jour",
            _ => "le téléphone n'a pas tout envoyé ou s'est tu",
        };
        _log.Log(session.HasError ? LogLevel.Warning : LogLevel.Information,
            "Sauvegarde de {Phone} terminée en {Duration:g} : {Photos} photo(s), {Videos} vidéo(s) ; {Outcome}",
            Phone(session.DeviceId), session.EndedAt - session.StartedAt, session.Photos, session.Videos, how);
    }
}
