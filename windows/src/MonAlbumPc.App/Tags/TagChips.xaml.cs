using System.Windows;
using System.Windows.Controls;
using MonAlbumPc.Core.Tags;

namespace MonAlbumPc.App.Tags;

/// <summary>A chip: a tag, and for a selection how many of its items carry it (« 3/5 »).</summary>
public sealed record TagChip(string Name, string CountText)
{
    public string RemoveText => $"Retirer le tag « {Name} »";
}

public partial class TagChips : UserControl
{
    public TagChips()
    {
        InitializeComponent();
        Box.Chosen += tag =>
        {
            Close();
            Added?.Invoke(tag);
        };
        Box.Cancelled += Close;
    }

    public static readonly DependencyProperty ChipBackgroundProperty =
        DependencyProperty.Register(nameof(ChipBackground), typeof(System.Windows.Media.Brush), typeof(TagChips));

    /// <summary>The fill of the chips: the theme's subtle fill on a page, a light veil over the dark full-screen viewer.</summary>
    public System.Windows.Media.Brush? ChipBackground
    {
        get => (System.Windows.Media.Brush?)GetValue(ChipBackgroundProperty);
        set => SetValue(ChipBackgroundProperty, value);
    }

    /// <summary>A tag to add to the photo or the selection.</summary>
    public event Action<string>? Added;

    /// <summary>A tag to remove from the photo or the selection.</summary>
    public event Action<string>? Removed;

    /// <summary>Shows <paramref name="tags"/>; <paramref name="of"/> items in all when they are those of a selection.</summary>
    public void Show(IReadOnlyList<(string Tag, int Count)> tags, IReadOnlyList<TagCount> library, int? of = null)
    {
        Chips.ItemsSource = tags.Select(t => new TagChip(t.Tag, of is { } total && t.Count < total ? $"{t.Count}/{total}" : "")).ToList();
        Box.Tags = library;
        Box.Exclude = of is null ? tags.Select(t => t.Tag).ToList() : tags.Where(t => t.Count == of).Select(t => t.Tag).ToList();
        AddLabel.Text = tags.Count == 0 ? "Ajouter un tag" : "Ajouter";
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        AddButton.Visibility = Visibility.Collapsed;
        Box.Visibility = Visibility.Visible;
        Box.Start();
    }

    private void Close()
    {
        Box.Visibility = Visibility.Collapsed;
        AddButton.Visibility = Visibility.Visible;
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            Removed?.Invoke(tag);
        }
    }
}
