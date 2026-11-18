using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MonAlbumPc.App;

/// <summary>Confirmations and messages in the style of a WinUI ContentDialog, in place of the MessageBox.</summary>
internal static class Dialogs
{
    /// <summary>
    /// Asks before acting; true when the user chose <paramref name="primary"/>. A destructive action is red and has
    /// to be clicked: the focus starts on "Annuler".
    /// </summary>
    public static bool Confirm(Window? owner, string title, string message, string primary, string? detail = null, bool destructive = false) =>
        Show(owner, new MessageDialog(title, message, detail, primary, destructive));

    /// <summary>Tells something that needs no decision, with a single OK button.</summary>
    public static void Inform(Window? owner, string title, string message, string? detail = null) =>
        Show(owner, new MessageDialog(title, message, detail, null, false));

    private static bool Show(Window? owner, MessageDialog dialog)
    {
        if (owner is { IsVisible: true } && owner.WindowState != WindowState.Minimized)
        {
            dialog.Owner = owner;
        }
        else
        {
            // Asked from the notification area, with the main window hidden: the dialog stands alone, in front.
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
            dialog.Topmost = true;
        }
        var content = dialog.Owner?.Content as UIElement;
        var layer = content is null ? null : AdornerLayer.GetAdornerLayer(content);
        var smoke = layer is null ? null : new Smoke(content!);
        if (smoke is not null)
        {
            layer!.Add(smoke);
        }
        try
        {
            return dialog.ShowDialog() == true;
        }
        finally
        {
            if (smoke is not null)
            {
                layer!.Remove(smoke);
            }
        }
    }

    /// <summary>Dims the owner window while the dialog is open, like the smoke layer of a ContentDialog.</summary>
    private sealed class Smoke(UIElement element) : Adorner(element)
    {
        protected override void OnRender(DrawingContext drawing) =>
            drawing.DrawRectangle(TryFindResource("ContentDialogSmokeFill") as Brush ?? new SolidColorBrush(Color.FromArgb(0x4D, 0, 0, 0)),
                null, new Rect(AdornedElement.RenderSize));
    }
}
