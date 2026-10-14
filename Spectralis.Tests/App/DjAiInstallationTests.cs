using Spectralis.App.Services;
using Spectralis.Core.Integrations;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class DjAiInstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "spectralis-ai-test-" + Guid.NewGuid().ToString("N"));
    private string Python => Path.Combine(_root, "venv", OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python");
    private string Checkpoint => Path.Combine(_root, "models", "hub", "checkpoints", "955717e8-8726e21a.th");
    private string Marker => Path.Combine(_root, "ready-v1");
    private static readonly ProcessResult Ready = new(0, "Ready", "", false);
    private static void Touch(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "test fixture"); }

    [Fact]
    public async Task Ready_marker_without_model_is_not_an_installation_and_never_downloads_during_validation()
    {
        Touch(Python); Touch(Marker);
        var ai = new DjStemSeparationService(_root, (_, _, _) => throw new Exception("Must not run or download"));
        Assert.False(ai.IsInstalled); Assert.True(ai.HasPartialInstallation); Assert.False(await ai.ValidateAsync());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ai.SeparateAsync("song.m4a", true, null, default));
        Assert.Contains("Install / repair", error.Message); Assert.False(File.Exists(Marker));
    }

    [Fact]
    public async Task Healthy_runtime_is_verified_and_recovers_its_missing_marker_without_reinstalling()
    {
        Touch(Python); Touch(Checkpoint); var calls = 0;
        var ai = new DjStemSeparationService(_root, (exe, args, _) =>
        {
            calls++; Assert.Equal(Python, exe); Assert.Contains("no_download", args[1]);
            Assert.Contains("hashlib.sha256", args[1]); return Task.FromResult(Ready);
        });
        await ai.InstallAsync(true, null, default);
        Assert.True(ai.IsInstalled); Assert.False(ai.HasPartialInstallation); Assert.Equal(1, calls);
        await ai.InstallAsync(true, null, default); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Partial_install_resumes_existing_python_and_finishes_the_model_before_marking_ready()
    {
        Touch(Python); Touch(Path.Combine(_root, OperatingSystem.IsWindows() ? "uv.exe" : "uv"));
        var commands = new List<string>();
        var ai = new DjStemSeparationService(_root, (_, args, _) =>
        {
            var command = string.Join(" ", args); commands.Add(command);
            Assert.False(File.Exists(Marker));
            if (command == "--version") return Task.FromResult(Ready with { Stdout = "uv 0.10.0" });
            if (command.Contains("print('Model ready')")) Touch(Checkpoint);
            return Task.FromResult(Ready);
        });
        await ai.InstallAsync(true, null, default);
        Assert.True(ai.IsInstalled); Assert.DoesNotContain(commands, c => c.StartsWith("venv "));
        Assert.Contains(commands, c => c.Contains("print('Model ready')"));
        Assert.Contains("no_download", commands.Last());
        Assert.False(Directory.Exists(Path.Combine(_root, "staging"))); Assert.False(Directory.Exists(Path.Combine(_root, "cache")));
    }

    [Fact]
    public async Task Broken_runtime_clears_stale_ready_marker_and_separation_never_repairs_implicitly()
    {
        Touch(Python); Touch(Checkpoint); Touch(Marker); var calls = 0;
        var ai = new DjStemSeparationService(_root, (_, args, _) =>
        {
            calls++; Assert.Contains("no_download", args[1]); return Task.FromResult(new ProcessResult(1, "", "broken runtime", false));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => ai.SeparateAsync("song.m4a", true, null, default));
        Assert.Equal(1, calls); Assert.False(ai.IsInstalled); Assert.False(File.Exists(Marker));
    }

    [Fact]
    public async Task Opt_out_does_not_create_files_or_run_any_installer()
    {
        var ai = new DjStemSeparationService(_root, (_, _, _) => throw new Exception("Must not execute"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ai.InstallAsync(false, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ai.SeparateAsync("song.m4a", false, null, default));
        Assert.False(Directory.Exists(_root));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
