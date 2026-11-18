using System.Windows;
using System.Windows.Media;

namespace MonAlbumPc.App;

/// <summary>The window shown by <see cref="Dialogs"/>.</summary>
public partial class MessageDialog : Window
{
    public MessageDialog(string title, string message, string? detail, string? primary, bool destructive)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Message.Text = message;
        if (detail is not null)
        {
            Detail.Text = detail;
            Detail.Visibility = Visibility.Visible;
        }
        if (primary is null)
        {
            // Nothing to decide: a single OK, on the right like the button it replaces.
            PrimaryButton.Visibility = Visibility.Hidden;
            SecondaryButton.Content = "OK";
            SecondaryButton.IsDefault = true;
        }
        else
        {
            PrimaryButton.Content = primary;
            PrimaryButton.IsDefault = !destructive;
            if (destructive)
            {
                PaintCritical();
            }
        }
        // Enter must not erase anything: a destructive action has to be chosen.
        Loaded += (_, _) => (primary is null || destructive ? SecondaryButton : PrimaryButton).Focus();
    }

    private void OnPrimary(object sender, RoutedEventArgs e) => DialogResult = true;

    // The accent button style reads its brushes by key, so the same keys on the button itself turn it red.
    private void PaintCritical()
    {
        var critical = ((SolidColorBrush)FindResource("SystemFillColorCriticalBrush")).Color;
        foreach (var (state, opacity) in new[] { ("", 1.0), ("PointerOver", 0.9), ("Pressed", 0.8) })
        {
            PrimaryButton.Resources[$"AccentButtonBackground{state}"] = new SolidColorBrush(critical) { Opacity = opacity };
            PrimaryButton.Resources[$"AccentButtonBorderBrush{state}"] = new SolidColorBrush(critical) { Opacity = opacity };
        }
        PrimaryButton.Resources["AccentControlElevationBorderBrush"] = new SolidColorBrush(critical);
        var text = ((SolidColorBrush)FindResource("TextOnAccentFillColorPrimaryBrush")).Color;
        PrimaryButton.Resources["AccentButtonForeground"] = new SolidColorBrush(text);
        PrimaryButton.Resources["AccentButtonForegroundPointerOver"] = new SolidColorBrush(text);
        PrimaryButton.Resources["AccentButtonForegroundPressed"] = new SolidColorBrush(text) { Opacity = 0.8 };
    }
}
