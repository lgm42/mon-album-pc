using System.Windows;
using MonAlbumPc.Core;

namespace MonAlbumPc.App;

public partial class App : Application
{
    private SingleInstance? _instance;
    private AppHost? _host;
    private TrayIcon? _tray;
    private MainWindow? _window;

    /// <summary>Set when the user quits, so the main window really closes instead of hiding.</summary>
    public static bool IsQuitting { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Themes.BrandTheme.Apply(this);
        _instance = SingleInstance.TryAcquire(() => Dispatcher.BeginInvoke(ShowMainWindow));
        if (_instance is null)
        {
            Shutdown();
            return;
        }

        _host = new AppHost(CoreServices.DefaultDataDirectory);
        await _host.StartAsync();

        _tray = new TrayIcon(_host, ShowMainWindow, ShowAddPhone, Quit);
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
        var answer = MessageBox.Show(
            "Les téléphones ne pourront plus envoyer leurs photos tant que Mon Album PC est fermé.\n\nQuitter quand même ?",
            "Quitter Mon Album PC", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            IsQuitting = true;
            Shutdown();
        }
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
        base.OnExit(e);
    }
}
