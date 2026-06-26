using Spectralis.Core.Integrations;
using Xunit;

namespace Spectralis.Tests.Core;

public class SafeProcessRunnerTests
{
    private static readonly string Cmd = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    [Fact]
    public async Task Run_CapturesStdoutAndExitCode()
    {
        var result = await SafeProcessRunner.RunAsync(
            Cmd, new[] { "/c", "echo", "hello" }, TimeSpan.FromSeconds(30));

        Assert.True(result.Succeeded);
        Assert.Contains("hello", result.Stdout);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Run_NonZeroExitCodeIsReported()
    {
        var result = await SafeProcessRunner.RunAsync(
            Cmd, new[] { "/c", "exit", "3" }, TimeSpan.FromSeconds(30));

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Run_HungProcessIsKilledOnTimeout()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await SafeProcessRunner.RunAsync(
            Cmd,
            new[] { "/c", "ping", "-n", "30", "127.0.0.1" },
            TimeSpan.FromSeconds(2));
        sw.Stop();

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"Kill took {sw.Elapsed}.");
    }

    [Fact]
    public async Task Run_OutputIsBounded()
    {
        var bigFile = Path.Combine(Path.GetTempPath(), $"spectralis-big-{Guid.NewGuid():N}.txt");
        File.WriteAllText(bigFile, new string('x', 200_000));
        try
        {
            var result = await SafeProcessRunner.RunAsync(
                Cmd,
                new[] { "/c", "type", bigFile },
                TimeSpan.FromSeconds(30),
                maxOutputBytes: 10_000);

            Assert.True(result.Stdout.Length <= 10_000);
            Assert.Equal(0, result.ExitCode); // child drained, not deadlocked
        }
        finally
        {
            File.Delete(bigFile);
        }
    }

    [Fact]
    public async Task Run_AdversarialArgumentArrivesAsSingleLiteralToken()
    {
        // ArgumentList must deliver hostile metadata as one literal argv entry —
        // command chaining must not execute. (cmd /c echo is the probe, so the
        // payload avoids raw quotes that cmd itself would re-parse.)
        const string hostile = "& del C:\\ & echo pwned";
        var result = await SafeProcessRunner.RunAsync(
            Cmd, new[] { "/c", "echo", hostile }, TimeSpan.FromSeconds(30));

        Assert.Contains("del C:\\", result.Stdout); // echoed back as text, not executed
    }
}

public class YtDlpArgumentTests
{
    [Fact]
    public void BuildStreamUrlArguments_UrlIsSingleTokenAfterSentinel()
    {
        const string url = "https://example.com/watch?v=abc&t=1";
        var args = YtDlpService.BuildStreamUrlArguments(url);

        var sentinelIndex = Array.IndexOf(args, "--");
        Assert.True(sentinelIndex >= 0);
        Assert.Equal(url, args[sentinelIndex + 1]); // untouched, unsplit, unescaped
        Assert.Equal(args.Length - 2, sentinelIndex);
    }

    [Theory]
    [InlineData("file:///C:/Windows/system32/config")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://host/file")]
    [InlineData("--exec=calc.exe")]
    [InlineData("not a url")]
    public void BuildStreamUrlArguments_RejectsNonHttpSources(string url)
    {
        Assert.Throws<ArgumentException>(() => YtDlpService.BuildStreamUrlArguments(url));
    }

    [Fact]
    public void BuildSearchArguments_AdversarialQueryStaysLiteral()
    {
        const string hostile = "song\" --exec \"calc.exe";
        var args = YtDlpService.BuildSearchArguments(hostile);

        var sentinelIndex = Array.IndexOf(args, "--");
        Assert.Equal($"ytsearch1:{hostile}", args[sentinelIndex + 1]);
        // The hostile text never becomes its own argument before the sentinel.
        Assert.DoesNotContain(args.Take(sentinelIndex), arg => arg.Contains("exec"));
    }
}
