using Spectralis.App.VideoExport;
using Xunit;

namespace Spectralis.Tests.App;

public class VideoExportQueueTests
{
    private static VideoExportRequest Req(string name) => new($"{name}.wav", name, "Artist", "Album", null, null);
    private static VideoExportOptions Opts(string path) => new() { OutputPath = path };

    [Fact]
    public async Task Runs_jobs_in_order_and_marks_them_done()
    {
        var order = new List<string>();
        var q = new VideoExportQueue((r, o, p, ct) => { order.Add(r.Title); return Task.CompletedTask; });
        q.Enqueue(Req("a"), Opts("a.mp4"));
        q.Enqueue(Req("b"), Opts("b.mp4"));

        await q.RunAsync(CancellationToken.None);

        Assert.Equal(["a", "b"], order);
        Assert.All(q.Jobs, j => Assert.Equal(VideoExportJobStatus.Done, j.Status));
    }

    [Fact]
    public async Task A_failed_job_does_not_stop_the_rest()
    {
        var q = new VideoExportQueue((r, o, p, ct) =>
            r.Title == "bad" ? throw new InvalidOperationException("ffmpeg died") : Task.CompletedTask);
        q.Enqueue(Req("bad"), Opts("bad.mp4"));
        q.Enqueue(Req("good"), Opts("good.mp4"));

        await q.RunAsync(CancellationToken.None);

        Assert.Equal(VideoExportJobStatus.Failed, q.Jobs[0].Status);
        Assert.Equal("ffmpeg died", q.Jobs[0].Error);
        Assert.Equal(VideoExportJobStatus.Done, q.Jobs[1].Status);
    }

    [Fact]
    public async Task Cancelling_marks_running_and_remaining_jobs_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var q = new VideoExportQueue(async (r, o, p, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
        });
        q.Enqueue(Req("a"), Opts("a.mp4"));
        q.Enqueue(Req("b"), Opts("b.mp4"));

        await q.RunAsync(cts.Token);

        Assert.All(q.Jobs, j => Assert.Equal(VideoExportJobStatus.Cancelled, j.Status));
    }

    [Fact]
    public void Duplicate_output_paths_get_a_numbered_suffix()
    {
        var q = new VideoExportQueue((r, o, p, ct) => Task.CompletedTask);
        var first = q.Enqueue(Req("a"), Opts(Path.Combine("out", "song.mp4")));
        var second = q.Enqueue(Req("a"), Opts(Path.Combine("out", "song.mp4")));
        var third = q.Enqueue(Req("a"), Opts(Path.Combine("out", "song.mp4")));

        Assert.Equal(Path.Combine("out", "song.mp4"), first.Options.OutputPath);
        Assert.Equal(Path.Combine("out", "song (2).mp4"), second.Options.OutputPath);
        Assert.Equal(Path.Combine("out", "song (3).mp4"), third.Options.OutputPath);
    }

    [Fact]
    public async Task Cannot_enqueue_while_running()
    {
        var gate = new TaskCompletionSource();
        var q = new VideoExportQueue((r, o, p, ct) => gate.Task);
        q.Enqueue(Req("a"), Opts("a.mp4"));
        var run = q.RunAsync(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => q.Enqueue(Req("b"), Opts("b.mp4")));

        gate.SetResult();
        await run;
    }
}
