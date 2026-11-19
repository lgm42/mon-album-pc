using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Activity;

namespace MonAlbumPc.App.Activity;

/// <summary>A background task, as a card of the page.</summary>
public sealed record WorkRow(string Title, string? Step, string? Item, string Pace, double Fraction, bool IsIndeterminate);

/// <summary>A line of the recent activity.</summary>
public sealed record EventRow(string Time, string Text, bool IsWarning, bool IsError);

/// <summary>What the app does in the background right now, and what happened recently; follows the board live.</summary>
public partial class ActivityPage : UserControl, IRefreshable
{
    private readonly CoreServices _core;
    private readonly bool _isViewer;

    // Elapsed and remaining times move even when a task is quiet.
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public ActivityPage(CoreServices core, bool isViewer)
    {
        _core = core;
        _isViewer = isViewer;
        InitializeComponent();
        _clock.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            _core.Work.Changed += OnChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _core.Work.Changed -= OnChanged;
            _clock.Stop();
        };
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void OnFilterChanged(object sender, RoutedEventArgs e) => Refresh();

    public void Refresh()
    {
        var now = _core.Clock.GetLocalNow();
        var running = _core.Work.Running;
        Running.ItemsSource = running.Select(w => new WorkRow(w.Title, w.Step, w.Item, WorkText.Pace(w, now), w.Fraction ?? 0,
            w.Fraction is null)).ToList();
        NoWork.Text = _isViewer
            ? "Aucune tâche en arrière-plan pour l'instant. La bibliothèque est vérifiée chaque minute."
            : "Aucune tâche en arrière-plan pour l'instant. L'index des positions et les autres travaux apparaîtront ici pendant qu'ils tournent.";
        NoWork.Visibility = running.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (running.Count > 0)
        {
            _clock.Start();
        }
        else
        {
            _clock.Stop();
        }

        var all = _core.Work.Events;
        var events = all.Where(e => ProblemsOnly.IsChecked != true || e.Level != WorkLevel.Info)
            .Select(e => new EventRow(WorkText.Time(e.At, now), e.Text, e.Level == WorkLevel.Warning, e.Level == WorkLevel.Error))
            .ToList();
        Events.ItemsSource = events;
        NoEvents.Text = all.Count == 0
            ? "Rien pour l'instant depuis le démarrage de Mon Album PC."
            : "Aucun avertissement ni erreur depuis le démarrage de Mon Album PC.";
        NoEvents.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
