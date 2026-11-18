using System.Diagnostics;
using System.Windows;
using MonAlbumPc.Core;

namespace MonAlbumPc.App;

public partial class App : Application
{
    private SingleInstance? _instance;
    private AppHost? _host;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private string[] _arguments = [];
    private bool _restarting;

    /// <summary>Set when the user quits, so the main window really closes instead of hiding.</summary>
    public static bool IsQuitting { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Themes.BrandTheme.Apply(this);
        // --data <folder>: separate catalog, for development and tests (one instance per catalog).
        _arguments = e.Args;
        var dataIndex = Array.IndexOf(e.Args, "--data");
        var customData = dataIndex >= 0 && dataIndex + 1 < e.Args.Length ? e.Args[dataIndex + 1] : null;
        var dataDirectory = customData ?? CoreServices.DefaultDataDirectory;

        _instance = SingleInstance.TryAcquire(() => Dispatcher.BeginInvoke(ShowMainWindow), customData);
        if (_instance is null)
        {
            Shutdown();
            return;
        }
        _host = new AppHost(dataDirectory);
        await _host.StartAsync();

        _tray = new TrayIcon(_host, ShowMainWindow, _host.IsViewer ? null : ShowAddPhone, Quit);
        _host.Core.Sessions.Ended += session => Dispatcher.BeginInvoke(() => _tray?.Announce(session));
        if (_host.Library is { } library)
        {
            library.Notified += notification => Dispatcher.BeginInvoke(() => _tray?.Show(notification));
            library.Start();
        }
        if (_host.ServerError is { } error)
        {
            _tray.ShowError("Mon Album PC", error);
        }

        if (!e.Args.Contains(Autostart.MinimizedArgument))
        {
            ShowMainWindow();
        }
    }

    public void ShowMainWindow()
    {
        _window ??= new MainWindow(_host!);
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }
        _window.Activate();
    }

    private void ShowAddPhone()
    {
        ShowMainWindow();
        _window!.ShowAddPhone(null);
    }

    private void Quit()
    {
        var consequence = _host!.IsViewer
            ? "Vous ne serez plus prévenu des nouvelles photos tant que Mon Album PC est fermé."
            : "Les téléphones ne pourront plus envoyer leurs photos tant que Mon Album PC est fermé.";
        if (Dialogs.Confirm(_window, "Quitter Mon Album PC ?", consequence, "Quitter"))
        {
            IsQuitting = true;
            Shutdown();
        }
    }

    /// <summary>Starts again in the mode just saved; the new instance is launched from <see cref="OnExit"/>.</summary>
    public void Restart()
    {
        _restarting = true;
        IsQuitting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_host is { } host)
        {
            // Off the UI thread: the dispatcher is shutting down and must not be awaited on.
            Task.Run(() => host.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));
        }
        _instance?.Dispose();
        if (_restarting)
        {
            // After releasing the single-instance lock, or the new process would only wake this one up. The window of a
            // restart is wanted, even for an app started minimized.
            var start = new ProcessStartInfo(Environment.ProcessPath!);
            foreach (var argument in _arguments.Where(a => a != Autostart.MinimizedArgument))
            {
                start.ArgumentList.Add(argument);
            }
            Process.Start(start);
        }
        base.OnExit(e);
    }
}
