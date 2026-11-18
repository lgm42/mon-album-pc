using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Library;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.App;

/// <summary>Icon in the notification area: status line, open, add a phone (when this PC receives photos), quit.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int MaxTooltip = 127;

    private readonly TaskbarIcon _icon;
    private readonly MenuItem _status;
    private readonly AppHost _host;

    public TrayIcon(AppHost host, Action open, Action? addPhone, Action quit)
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
            },
        };
        if (addPhone is not null)
        {
            menu.Items.Add(Item("Ajouter un téléphone…", addPhone));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quitter", quit));
        menu.Opened += (_, _) => _status.Header = StatusText();

        _icon = new TaskbarIcon
        {
            ToolTipText = "Mon Album PC",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")),
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };
        _icon.TrayLeftMouseUp += (_, _) => open();
        _icon.TrayBalloonTipClicked += (_, _) => open();
        // Efficiency mode would throttle the process, which also hosts the server receiving photos.
        _icon.ForceCreate(enablesEfficiencyMode: false);
        if (host.Library is null)
        {
            host.Core.Sessions.Changed += OnSessionsChanged;
        }
    }

    // Hovering the icon tells what is arriving without opening the window.
    private void OnSessionsChanged() => _icon.Dispatcher.BeginInvoke(() =>
    {
        var text = _host.Core.Sessions.Active.Count == 0 ? "Mon Album PC" : StatusText();
        // The notification area cuts tooltips at 127 characters.
        _icon.ToolTipText = text.Length <= MaxTooltip ? text : text[..(MaxTooltip - 1)] + "…";
    });

    public void ShowError(string title, string message) => _icon.ShowNotification(title, message, NotificationIcon.Warning);

    public void ShowInfo(string title, string message) => _icon.ShowNotification(title, message, NotificationIcon.Info);

    public void Show(LibraryNotification notification)
    {
        if (notification.IsWarning)
        {
            ShowError(notification.Title, notification.Message);
        }
        else
        {
            ShowInfo(notification.Title, notification.Message);
        }
    }

    /// <summary>Windows notification at the end of a backup: what arrived, or why it stopped.</summary>
    public void Announce(BackupSession session)
    {
        var phone = _host.Core.Devices.Get(session.DeviceId)?.Name ?? "Le téléphone";
        var album = _host.Core.Albums.Get(session.AlbumId)?.Name ?? "";
        if (session.HasError)
        {
            ShowError(session.ErrorCode == SessionErrors.ImportFailed ? ActivityNotifications.FailedFilesTitle(phone) : $"Sauvegarde de {phone} interrompue",
                session.ErrorMessage ?? "");
            return;
        }
        if (session.Complete)
        {
            ShowInfo(ActivityNotifications.CompleteTitle(phone), ActivityNotifications.Detail(session.Photos, session.Videos, album));
            return;
        }
        var parts = new List<string>();
        if (session.Photos > 0) parts.Add(Formatting.Plural(session.Photos, "photo", "photos"));
        if (session.Videos > 0) parts.Add(Formatting.Plural(session.Videos, "vidéo", "vidéos"));
        ShowInfo("Sauvegarde terminée", $"{string.Join(" et ", parts)} de {phone} rangées dans l'album {album}.");
    }

    public void Dispose()
    {
        _host.Core.Sessions.Changed -= OnSessionsChanged;
        _icon.Dispose();
    }

    private string StatusText()
    {
        if (_host.Library is { } library)
        {
            return library.State == LibraryState.Unavailable ? "Bibliothèque indisponible" : "Bibliothèque connectée";
        }
        if (_host.Core.Sessions.Active.FirstOrDefault() is { } active)
        {
            var phone = _host.Core.Devices.Get(active.DeviceId)?.Name ?? "un téléphone";
            return SessionStatusText.Compact(active, phone, _host.Core.Clock.GetLocalNow());
        }
        return _host.ServerError is null ? "Prêt à recevoir les photos" : "Les téléphones ne peuvent pas joindre ce PC";
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
