using System.Windows;
using System.Windows.Controls;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Activity;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Tags;

namespace MonAlbumPc.App.Tags;

/// <summary>A line of the tag tree; a parent that is not a tag itself only groups its children.</summary>
public sealed record TagRow(string Name, string Leaf, int Depth, string CountText)
{
    public Thickness Indent => new(12 + 20 * Depth, 0, 0, 0);
    public string ShowText => $"Voir les photos de « {Name} »";
}

/// <summary>A tag operation in progress: "Renommage du tag « boulot » en « Travail » · 34 / 120 · IMG_1.jpg".</summary>
public sealed record RunningLine(string Text, double Fraction, bool Indeterminate);

/// <summary>The tags of the library as a tree: create (also under a tag), rename, merge into another, delete.</summary>
public partial class TagsPage : UserControl, IRefreshable
{
    private readonly CoreServices _core;
    private readonly MainWindow _window;
    private Dictionary<string, int> _counts = new(UserTags.Same);
    private IReadOnlyList<TagCount> _tags = [];

    // What the editor does with the name chosen: create (under a parent or not), or rename this tag.
    private string? _renaming;

    public TagsPage(CoreServices core, MainWindow window)
    {
        _core = core;
        _window = window;
        InitializeComponent();
        EditorBox.Chosen += OnEditorChosen;
        EditorBox.Cancelled += CloseEditor;
        // Both raised from background threads.
        _core.Tags.Changed += () => Dispatcher.BeginInvoke(Refresh);
        _core.Work.Changed += () => Dispatcher.BeginInvoke(ShowRunning);
    }

    public async void Refresh()
    {
        try
        {
            _tags = await Task.Run(_core.Tags.List);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return; // the catalog is busy: the next change brings them
        }
        _counts = _tags.ToDictionary(t => t.Name, t => t.Count, UserTags.Same);
        // Parents that are not tags themselves still show, to hold their children.
        TagList.ItemsSource = TagSelection.Entries(_tags.Select(t => t.Name), [])
            .Select(e => new TagRow(e.Name, e.Leaf, e.Depth,
                _counts.TryGetValue(e.Name, out var count) ? Formatting.Plural(count, "photo", "photos") : "groupe"))
            .ToList();
        EmptyState.Visibility = _tags.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TreeCard.Visibility = _tags.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EditorBox.Tags = _tags;
        ShowRunning();
    }

    private void ShowRunning() => RunningList.ItemsSource = _core.Work.Running.Where(TagQuestions.IsTagWork)
        .Select(w => new RunningLine(WorkText.Compact(w), w.Fraction ?? 0, w.Fraction is null))
        .ToList();

    // ---- Editor ----

    private void OnNewTag(object sender, RoutedEventArgs e) => OpenEditor("Nouveau tag", "", null);

    private void OnNewChild(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            OpenEditor($"Nouveau sous-tag de « {tag} »", tag + UserTags.Separator, null);
        }
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            OpenEditor($"Renommer « {tag} » (un tag existant : fusionner)", tag, tag);
        }
    }

    private void OpenEditor(string title, string initial, string? renaming)
    {
        _renaming = renaming;
        EditorTitle.Text = title;
        EditorBox.Tags = _tags;
        EditorBox.Exclude = renaming is null ? [] : [renaming];
        EditorBox.Placeholder = renaming is null ? "Nom du tag (« / » pour un sous-tag)" : "Nouveau nom, ou un tag existant";
        Editor.Visibility = Visibility.Visible;
        EditorBox.Start(initial);
    }

    private void OnCancelEditor(object sender, RoutedEventArgs e) => CloseEditor();

    private void CloseEditor()
    {
        Editor.Visibility = Visibility.Collapsed;
        _renaming = null;
    }

    private async void OnEditorChosen(string typed)
    {
        var renaming = _renaming;
        CloseEditor();
        if (renaming is not null)
        {
            await RenameAsync(renaming, typed);
            return;
        }
        if (_core.Tags.Canonical(typed) is { } existing && _counts.ContainsKey(existing))
        {
            Dialogs.Inform(_window, "Ce tag existe déjà", $"Le tag « {existing} » existe déjà.");
            return;
        }
        await _core.Tags.CreateAsync(typed);
        Refresh();
    }

    // ---- Rename, merge, delete ----

    private async Task RenameAsync(string from, string typed)
    {
        if (_core.Tags.Canonical(typed) is not { } to || string.Equals(to, from, StringComparison.Ordinal))
        {
            return;
        }
        if (UserTags.IsWithin(to, from) && !UserTags.Same.Equals(to, from))
        {
            Dialogs.Inform(_window, "Renommage impossible", $"« {to} » est sous « {from} » : un tag ne peut pas devenir son propre sous-tag.");
            return;
        }
        // Counted now: photos may have been tagged since the page was shown.
        var (count, children, already) = await Task.Run(() => (
            _core.Tags.CountWithin(from),
            _core.Tags.Children(from).Count,
            !UserTags.Same.Equals(from, to) && _core.Tags.Exists(to) ? _core.Tags.CountWithin(to) : (int?)null));
        var question = TagQuestions.Rename(from, to, count, children, already);
        if (Dialogs.Confirm(_window, question.Title, question.Message, question.Action, question.Detail))
        {
            await RunAsync(_core.Tags.RenameAsync(from, to));
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }
        CloseEditor();
        var (count, children) = await Task.Run(() => (_core.Tags.CountWithin(tag), _core.Tags.Children(tag).Count));
        var question = TagQuestions.Delete(tag, count, children);
        if (Dialogs.Confirm(_window, question.Title, question.Message, question.Action, question.Detail, destructive: true))
        {
            await RunAsync(_core.Tags.DeleteAsync(tag));
        }
    }

    /// <summary>Waits for an operation running in the background (its progress shows above), then tells how it went.</summary>
    private async Task RunAsync(Task<TagRunResult> operation)
    {
        ShowRunning();
        TagRunResult result;
        try
        {
            result = await operation;
        }
        catch (Exception e)
        {
            Dialogs.Inform(_window, "L'opération sur les tags a échoué", e.Message);
            return;
        }
        Refresh();
        if (result.Failures.Count == 0)
        {
            Dialogs.Inform(_window, "Tags", result.Summary + ".");
            return;
        }
        var lines = result.Failures.Take(5).Select(f => $"{f.Name} : {f.Reason}").ToList();
        if (result.Failures.Count > lines.Count)
        {
            lines.Add($"… et {Formatting.Plural(result.Failures.Count - lines.Count, "autre", "autres")}");
        }
        Dialogs.Inform(_window, "Tags modifiés en partie", result.Summary + ".", string.Join("\n", lines));
    }

    private void OnShowPhotos(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            _window.ShowTag(tag);
        }
    }
}
