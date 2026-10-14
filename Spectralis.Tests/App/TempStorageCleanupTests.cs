using Spectralis.App.Services;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class TempStorageCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "spectralis-gc-test-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime _cutoff = DateTime.UtcNow.AddDays(-2);
    private string Temp => Path.Combine(_root, "temp");
    private string Ai => Path.Combine(_root, "ai");
    public TempStorageCleanupTests() { Directory.CreateDirectory(Temp); Directory.CreateDirectory(Ai); }
    public void Dispose() => Directory.Delete(_root, true);
    private string FileAt(string relative, bool old = true)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "preserve or collect");
        File.SetLastWriteTimeUtc(path, old ? _cutoff.AddDays(-10) : DateTime.UtcNow);
        return path;
    }
    private void AgeTree(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(file, _cutoff.AddDays(-10));
        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)) Directory.SetLastWriteTimeUtc(directory, _cutoff.AddDays(-10));
        Directory.SetLastWriteTimeUtc(path, _cutoff.AddDays(-10));
    }

    [Fact]
    public void Sweep_deletes_only_recognized_old_unlocked_decode_files()
    {
        var old = FileAt("temp/spectralis-decode-" + Guid.NewGuid().ToString("N") + ".wav");
        var recent = FileAt("temp/spectralis-decode-" + Guid.NewGuid().ToString("N") + ".wav", false);
        var locked = FileAt("temp/spectralis-decode-" + Guid.NewGuid().ToString("N") + ".wav");
        var unrelated = FileAt("temp/spectralis-decode-my-song.wav");
        using var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = TempStorageCleanup.Sweep(Temp, Ai, _cutoff);
        Assert.Equal(1, result.Files); Assert.True(result.Bytes > 0);
        Assert.False(File.Exists(old));
        Assert.All(new[] { recent, locked, unrelated }, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void Sweep_preserves_active_jobs_new_jobs_models_and_unrelated_data()
    {
        var old = FileAt("temp/Spectralis/dj-separation/old/stem.wav");
        var active = FileAt("temp/Spectralis/dj-separation/active/stem.wav");
        var leasePath = FileAt("temp/Spectralis/dj-separation/active/.active");
        var recent = FileAt("temp/Spectralis/dj-separation/recent/stem.wav", false);
        var staging = FileAt("ai/staging/installer.zip");
        var cache = FileAt("ai/cache/wheel.whl");
        var model = FileAt("ai/models/model.th");
        var runtime = FileAt("ai/python/python.exe");
        var remoteCache = FileAt("temp/Spectralis/audio-cache/song.m4a");
        AgeTree(Path.GetDirectoryName(old)!); AgeTree(Path.GetDirectoryName(active)!);
        AgeTree(Path.Combine(Ai, "staging")); AgeTree(Path.Combine(Ai, "cache"));
        using var lease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        TempStorageCleanup.Sweep(Temp, Ai, _cutoff);
        Assert.All(new[] { old, staging, cache }, path => Assert.False(File.Exists(path)));
        Assert.All(new[] { active, recent, model, runtime, remoteCache }, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void Ai_install_lease_preserves_staging_until_install_completes()
    {
        var staging = FileAt("ai/staging/installer.zip"); AgeTree(Path.Combine(Ai, "staging"));
        var leasePath = FileAt("ai/.active");
        using (var lease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            TempStorageCleanup.Sweep(Temp, Ai, _cutoff);
            Assert.True(File.Exists(staging));
        }
        TempStorageCleanup.Sweep(Temp, Ai, _cutoff);
        Assert.False(File.Exists(staging));
    }

    [Fact]
    public void Package_collection_keeps_current_pending_rollback_recent_and_unknown_packages()
    {
        var old = FileAt("packages/Spectralis-1.0.0-full.nupkg");
        var delta = FileAt("packages/Spectralis-1.0.0-win-x64-delta.nupkg");
        var rollback = FileAt("packages/Spectralis-1.1.0-win-x64-full.nupkg");
        var current = FileAt("packages/Spectralis-1.2.0-full.nupkg");
        var pending = FileAt("packages/Spectralis-1.3.0-full.nupkg");
        var recent = FileAt("packages/Spectralis-0.9.0-full.nupkg", false);
        var unknown = FileAt("packages/AnotherApp-0.1.0-full.nupkg");
        var prerelease = FileAt("packages/Spectralis-1.4.0-beta-full.nupkg");
        var result = TempStorageCleanup.CleanupOldPackages(Path.Combine(_root, "packages"), "1.2.0", _cutoff);
        Assert.Equal(2, result.Files); Assert.False(File.Exists(old)); Assert.False(File.Exists(delta));
        Assert.All(new[] { rollback, current, pending, recent, unknown, prerelease }, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void Deletion_rejects_root_itself_parent_and_sibling_prefixes()
    {
        var child = FileAt("ai/staging/delete.zip");
        var sibling = FileAt("ai-other/keep.txt");
        Assert.Equal(0, TempStorageCleanup.DeleteOwnedTree(Ai, _root).Files);
        Assert.Equal(0, TempStorageCleanup.DeleteOwnedTree(Ai, Ai).Files);
        Assert.Equal(0, TempStorageCleanup.DeleteOwnedTree(Ai, Path.GetDirectoryName(sibling)!).Files);
        Assert.Equal(1, TempStorageCleanup.DeleteOwnedTree(Ai, Path.GetDirectoryName(child)!).Files);
        Assert.True(File.Exists(sibling));
    }

    [Fact]
    public void Empty_package_directory_and_nonstable_current_version_are_safe()
    {
        Assert.Equal(0, TempStorageCleanup.CleanupOldPackages(Ai, "1.0.0", _cutoff).Files);
        var file = FileAt("ai/Spectralis-1.0.0-full.nupkg");
        Assert.Equal(0, TempStorageCleanup.CleanupOldPackages(Ai, "2.0.0-beta", _cutoff).Files);
        Assert.True(File.Exists(file));
    }
}
