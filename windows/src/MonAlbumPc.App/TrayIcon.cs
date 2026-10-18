using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace MonAlbumPc.App;

/// <summary>Icon in the notification area: status line, open, add a phone, quit.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _status;
    private readonly AppHost _host;

    public TrayIcon(AppHost host, Action open, Action addPhone, Action quit)
    {
        _host = host;
        _status = new MenuItem { IsEnabled = false };
        var menu = new ContextMenu
        {
            Items =
            {
                _status,
                new Separator(),
                Item("Ouvrir Mon Album PC", open),
                Item("Ajouter un téléphone…", addPhone),
                new Separator(),
                Item("Quitter", quit),
            },
        };
        menu.Opened += (_, _) => _status.Header = StatusText();

        _icon = new TaskbarIcon
        {
            ToolTipText = "Mon Album PC",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")),
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };
        _icon.TrayLeftMouseUp += (_, _) => open();
        // Efficiency mode would throttle the process, which also hosts the server receiving photos.
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void ShowError(string title, string message) => _icon.ShowNotification(title, message, NotificationIcon.Warning);

    public void ShowInfo(string title, string message) => _icon.ShowNotification(title, message, NotificationIcon.Info);

    public void Dispose() => _icon.Dispose();

    private string StatusText() => _host.ServerError is null ? "Prêt à recevoir les photos" : "Les téléphones ne peuvent pas joindre ce PC";

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
