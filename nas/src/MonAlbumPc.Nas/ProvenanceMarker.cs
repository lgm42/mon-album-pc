using System.Diagnostics;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Nas;

public enum ProvenanceMarkerState { Idle, Planning, Planned, Marking, Done, Failed }

/// <summary>
/// The administrator's « Marquer la provenance des fichiers existants »: works out what would change, then, once confirmed,
/// marks the photos on a low-priority thread. Lives as long as the server, so leaving the page does not stop it.
/// </summary>
public sealed class ProvenanceMarker(CoreServices core, ILogger<ProvenanceMarker> logger)
{
    private readonly Lock _lock = new();
    private readonly Stopwatch _watch = new();
    private CancellationTokenSource? _cancel;

    public ProvenanceMarkerState State { get; private set; }

    public ProvenanceProgress Progress { get; private set; }

    public ProvenancePlan? Plan { get; private set; }

    public ProvenanceSummary? Summary { get; private set; }

    public string? Error { get; private set; }

    public TimeSpan Elapsed => _watch.Elapsed;

    /// <summary>Time left while marking, once enough files are done to tell.</summary>
    public TimeSpan? Remaining => State == ProvenanceMarkerState.Marking && Progress is { Done: >= 20, Total: > 0 } p
        ? _watch.Elapsed * ((p.Total - p.Done) / (double)p.Done)
        : null;

    public bool IsBusy => State is ProvenanceMarkerState.Planning or ProvenanceMarkerState.Marking;

    /// <summary>Raised from the worker thread at every step.</summary>
    public event Action? Changed;

    public void Prepare()
    {
        if (!TryStart(ProvenanceMarkerState.Planning))
        {
            return;
        }
        Plan = null;
        Summary = null;
        logger.LogInformation("Provenance : analyse des albums");
        Start(token =>
        {
            Plan = ProvenanceBackfill.Plan(core.History, core.Albums.List(), new Reporter(this), token);
            logger.LogInformation("Provenance : {Files} photos à marquer, {Already} déjà marquées, {Formats} dans un autre format",
                Plan.Files.Count, Plan.AlreadyTagged, Plan.OtherFormats);
            return ProvenanceMarkerState.Planned;
        });
    }

    public void Mark()
    {
        if (Plan is not { } plan || State != ProvenanceMarkerState.Planned || !TryStart(ProvenanceMarkerState.Marking))
        {
            return;
        }
        logger.LogInformation("Provenance : marquage de {Files} photos", plan.Files.Count);
        Start(token =>
        {
            var summary = ProvenanceBackfill.Run(plan, new Reporter(this), token);
            Summary = summary;
            Plan = null;
            logger.Log(summary.Problems.Count > 0 ? LogLevel.Warning : LogLevel.Information,
                "Provenance : {Tagged} photos marquées, {Already} déjà marquées, {Problems} laissées telles quelles en {Seconds:0} s{First}",
                summary.Tagged, summary.AlreadyTagged, summary.Problems.Count, summary.Duration.TotalSeconds,
                summary.Problems.Count == 0 ? "" : $" (première : {summary.Problems[0].Path} — {summary.Problems[0].Reason})");
            // Sizes changed: the index reads these photos again (their dates are kept, thumbnails stay valid).
            core.MediaIndexer.RequestRefresh();
            return ProvenanceMarkerState.Done;
        });
    }

    public void Cancel() => _cancel?.Cancel();

    /// <summary>Back to the start, to look again.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            if (!IsBusy)
            {
                State = ProvenanceMarkerState.Idle;
                Plan = null;
                Summary = null;
                Error = null;
            }
        }
        Changed?.Invoke();
    }

    private bool TryStart(ProvenanceMarkerState state)
    {
        lock (_lock)
        {
            if (IsBusy)
            {
                return false;
            }
            State = state;
            Error = null;
            Progress = new ProvenanceProgress("Préparation…", 0, 0);
            return true;
        }
    }

    private void Start(Func<CancellationToken, ProvenanceMarkerState> work)
    {
        var cancel = _cancel = new CancellationTokenSource();
        _watch.Restart();
        Changed?.Invoke();
        new Thread(() =>
        {
            try
            {
                State = work(cancel.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Provenance : interrompu par l'administrateur");
                State = ProvenanceMarkerState.Idle;
                Plan = null;
            }
            catch (Exception e)
            {
                // Out of a background thread, an exception would stop the whole server.
                logger.LogError(e, "Provenance : interrompu");
                Error = e.Message;
                State = ProvenanceMarkerState.Failed;
            }
            _watch.Stop();
            Changed?.Invoke();
        }) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "Provenance" }.Start();
    }

    private sealed class Reporter(ProvenanceMarker marker) : IProgress<ProvenanceProgress>
    {
        public void Report(ProvenanceProgress value)
        {
            marker.Progress = value;
            marker.Changed?.Invoke();
        }
    }
}
