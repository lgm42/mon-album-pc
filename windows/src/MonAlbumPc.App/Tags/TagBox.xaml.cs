using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Tags;

namespace MonAlbumPc.App.Tags;

/// <summary>A line of the proposals of a <see cref="TagBox"/>.</summary>
public sealed record TagSuggestion(string Name, string Label, string Detail, bool IsCreate);

public partial class TagBox : UserControl
{
    private const int MaxSuggestions = 8;
    private static readonly CompareInfo French = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public TagBox()
    {
        InitializeComponent();
    }

    /// <summary>The tags of the library to propose.</summary>
    public IReadOnlyList<TagCount> Tags { get; set; } = [];

    /// <summary>Tags not to propose (those already on the photo).</summary>
    public IEnumerable<string> Exclude { get; set; } = [];

    /// <summary>Proposes the most used tags before anything is typed.</summary>
    public bool SuggestFirst { get; set; }

    public string Placeholder
    {
        get => Hint.Text;
        set => Hint.Text = value;
    }

    /// <summary>The tag chosen or typed, tidied (« Bricolage / ESP32 » → « Bricolage/ESP32 »).</summary>
    public event Action<string>? Chosen;

    public event Action? Cancelled;

    /// <summary>Empties the box (or puts <paramref name="initial"/> in it), shows the proposals and takes the focus.</summary>
    public void Start(string initial = "")
    {
        Input.Text = initial;
        Input.CaretIndex = initial.Length;
        Update();
        Dispatcher.BeginInvoke(() => Input.Focus(), DispatcherPriority.Input);
    }

    private string? Typed => UserTags.Normalize(Input.Text);

    private void OnTextChanged(object sender, TextChangedEventArgs e) => Update();

    private void Update()
    {
        Hint.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var text = Input.Text.Trim();
        var exclude = Exclude.ToHashSet(UserTags.Same);
        List<TagSuggestion> list = text.Length == 0 && !SuggestFirst ? [] :
        [
            .. Tags
                .Where(t => !exclude.Contains(t.Name) && (text.Length == 0 || French.IndexOf(t.Name, text, Loose) >= 0))
                .OrderBy(t => text.Length > 0 && French.IsPrefix(t.Name, text, Loose) ? 0 : 1)
                .ThenByDescending(t => t.Count)
                .ThenBy(t => t.Name, TagCatalog.TagOrder.Instance)
                .Take(MaxSuggestions)
                .Select(t => new TagSuggestion(t.Name, t.Name, Formatting.Plural(t.Count, "photo", "photos"), false)),
        ];
        if (Typed is { } typed && !list.Any(s => UserTags.Same.Equals(s.Name, typed)) && !exclude.Contains(typed))
        {
            list.Add(new TagSuggestion(typed, $"+  Créer le tag « {typed} »", "", true));
        }
        Suggestions.ItemsSource = list;
        Suggestions.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var count = Suggestions.Items.Count;
        switch (e.Key)
        {
            case Key.Down when count > 0:
                Suggestions.SelectedIndex = (Suggestions.SelectedIndex + 1) % count;
                Suggestions.ScrollIntoView(Suggestions.SelectedItem);
                break;
            case Key.Up when count > 0:
                Suggestions.SelectedIndex = Suggestions.SelectedIndex <= 0 ? count - 1 : Suggestions.SelectedIndex - 1;
                Suggestions.ScrollIntoView(Suggestions.SelectedItem);
                break;
            case Key.Escape:
                Cancelled?.Invoke();
                break;
            case Key.Enter:
                if (Suggestions.SelectedItem is TagSuggestion chosen)
                {
                    Choose(chosen.Name);
                }
                else if (Typed is { } typed)
                {
                    // An existing tag typed in another case keeps the library's spelling.
                    Choose(Tags.FirstOrDefault(t => UserTags.Same.Equals(t.Name, typed))?.Name ?? typed);
                }
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnSuggestionClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is TagSuggestion suggestion)
        {
            Choose(suggestion.Name);
        }
    }

    private void Choose(string tag)
    {
        Input.Text = "";
        Chosen?.Invoke(tag);
    }
}
