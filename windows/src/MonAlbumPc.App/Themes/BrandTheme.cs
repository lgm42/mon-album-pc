using System.Windows;
using Microsoft.Win32;

namespace MonAlbumPc.App.Themes;

/// <summary>
/// The Fluent theme resolves its accent brushes from the system accent once, so the brand indigo is applied by
/// overriding those brushes, light or dark like Windows.
/// </summary>
internal static class BrandTheme
{
    private static ResourceDictionary? _current;

    public static void Apply(Application app)
    {
        Update(app);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                app.Dispatcher.BeginInvoke(() => Update(app));
            }
        };
    }

    private static void Update(Application app)
    {
        var name = IsLightTheme() ? "Brand.Light.xaml" : "Brand.Dark.xaml";
        var dictionary = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{name}") };
        if (_current is not null)
        {
            app.Resources.MergedDictionaries.Remove(_current);
        }
        app.Resources.MergedDictionaries.Add(dictionary);
        _current = dictionary;
    }

    private static bool IsLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
}
