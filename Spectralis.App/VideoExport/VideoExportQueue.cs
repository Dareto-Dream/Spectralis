namespace Spectralis.App.VideoExport;

public enum VideoExportJobStatus { Pending, Running, Done, Failed, Cancelled }

/// <summary>One queued export. Status/progress/error are written by the queue as it runs.</summary>
public sealed class VideoExportJob
{
    public VideoExportJob(VideoExportRequest request, VideoExportOptions options)
    {
        Request = request;
        Options = options;
    }

    public VideoExportRequest Request { get; }
    public VideoExportOptions Options { get; }
    public VideoExportJobStatus Status { get; internal set; } = VideoExportJobStatus.Pending;
    public float Progress { get; internal set; }
    public string? Error { get; internal set; }

    public string Label => $"{Request.Artist} — {Request.Title}  [{Options.Width}×{Options.Height}]";
}

/// <summary>
/// Runs exports one after another (they each own FFmpeg and, for HTML visualizers, a WebView, so
/// parallel runs would just fight over the machine). A failed job doesn't stop the rest.
/// </summary>
public sealed class VideoExportQueue
{
    public delegate Task Exporter(
        VideoExportRequest request, VideoExportOptions options, IProgress<float>? progress, CancellationToken ct);

    private readonly List<VideoExportJob> _jobs = [];
    private readonly Exporter _exporter;
    private bool _running;

    public static VideoExportQueue Shared { get; } = new();

    public VideoExportQueue(Exporter? exporter = null) => _exporter = exporter ?? VideoExportEngine.ExportAsync;

    public IReadOnlyList<VideoExportJob> Jobs => _jobs;
    public bool IsRunning => _running;

    /// <summary>Raised on whatever thread changed a job; UI code should marshal.</summary>
    public event Action? Changed;

    public VideoExportJob Enqueue(VideoExportRequest request, VideoExportOptions options)
    {
        if (_running)
            throw new InvalidOperationException("The queue is running; wait for it to finish before adding jobs.");

        // Two queued jobs must never write the same file.
        options.OutputPath = UniquePath(options.OutputPath, _jobs.Select(j => j.Options.OutputPath));
        var job = new VideoExportJob(request, options);
        _jobs.Add(job);
        Changed?.Invoke();
        return job;
    }

    public void ClearFinished()
    {
        if (_running)
            return;
        _jobs.RemoveAll(j => j.Status is not VideoExportJobStatus.Pending);
        Changed?.Invoke();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (_running)
            throw new InvalidOperationException("The queue is already running.");
        _running = true;
        try
        {
            foreach (var job in _jobs.Where(j => j.Status == VideoExportJobStatus.Pending).ToList())
            {
                if (ct.IsCancellationRequested)
                {
                    job.Status = VideoExportJobStatus.Cancelled;
                    continue;
                }

                job.Status = VideoExportJobStatus.Running;
                Changed?.Invoke();
                var progress = new Progress<float>(p => { job.Progress = p; Changed?.Invoke(); });
                try
                {
                    await _exporter(job.Request, job.Options, progress, ct);
                    job.Progress = 1f;
                    job.Status = VideoExportJobStatus.Done;
                }
                catch (OperationCanceledException)
                {
                    job.Status = VideoExportJobStatus.Cancelled;
                }
                catch (Exception ex)
                {
                    job.Error = ex.Message;
                    job.Status = VideoExportJobStatus.Failed;
                }
                Changed?.Invoke();
            }
        }
        finally
        {
            _running = false;
            Changed?.Invoke();
        }
    }

    internal static string UniquePath(string path, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(path))
            return path;

        var dir = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!used.Contains(candidate))
                return candidate;
        }
    }
}
