using System.Text.RegularExpressions;
using Velopack.Locators;

namespace Spectralis.App.Services;

public sealed record TempCleanupResult(int Files, long Bytes)
{
    public override string ToString() => $"Removed {Files} old temporary files ({Bytes / 1048576.0:0.#} MB).";
}

/// <summary>Collect only known Spectralis scratch data. Never follows links, scans
/// Downloads, removes the installed model, or touches a current/pending update.</summary>
public static class TempStorageCleanup
{
    private static readonly SemaphoreSlim SweepGate = new(1, 1);
    public static async Task<TempCleanupResult> RunAsync()
    {
        await SweepGate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var result = Sweep(Path.GetTempPath(), DjStemSeparationService.InstallRoot, DateTime.UtcNow - TimeSpan.FromDays(2));
                try
                {
                    var locator = VelopackLocator.GetDefault(null!);
                    if (locator.AppId?.Equals("Spectralis", StringComparison.OrdinalIgnoreCase) == true &&
                        locator.CurrentlyInstalledVersion is { } version && locator.PackagesDir is { } packages)
                    {
                        var packagesResult = CleanupOldPackages(packages, version.ToString(), DateTime.UtcNow - TimeSpan.FromDays(7));
                        result = new(result.Files + packagesResult.Files, result.Bytes + packagesResult.Bytes);
                        if (locator.AppTempDir is { } scratch && IsSafeDescendant(packages, scratch) &&
                            SamePath(scratch, Path.Combine(packages, "VelopackTemp")) && !SamePath(scratch, locator.AppContentDir))
                        {
                            // Updater owns its temp directory; only stale immediate children are candidates.
                            var tmpResult = SweepChildren(scratch, DateTime.UtcNow - TimeSpan.FromDays(7));
                            result = new(result.Files + tmpResult.Files, result.Bytes + tmpResult.Bytes);
                        }
                    }
                }
                catch { /* Development/portable builds have no installed updater. */ }
                SpectralisLog.Info("Temporary storage cleanup: " + result);
                return result;
            });
        }
        finally { SweepGate.Release(); }
    }

    public static TempCleanupResult Sweep(string tempRoot, string aiRoot, DateTime olderThan)
    {
        var files = 0; long bytes = 0;
        if (Directory.Exists(tempRoot) && !IsLink(tempRoot))
        {
            foreach (var file in Directory.EnumerateFiles(tempRoot, "spectralis-decode-*.wav"))
                if (Regex.IsMatch(Path.GetFileName(file), @"^spectralis-decode-[a-fA-F0-9]{32}\.wav$") && File.GetLastWriteTimeUtc(file) < olderThan)
                    Add(DeleteFile(tempRoot, file));
            foreach (var relative in new[] { "Spectralis/dj-separation", "Spectralis/dj-download", "Spectralis/song-download", "spectralis/video-export", "Spectralis/install-staging", "Spectralis/shared-play-uploads" })
            {
                var root = Path.Combine(tempRoot, relative);
                if (!IsSafeDescendant(tempRoot, root)) continue;
                Add(SweepChildren(root, olderThan));
            }
        }
        if (!HasActiveLease(aiRoot))
            foreach (var name in new[] { "staging", "cache" })
            {
                var path = Path.Combine(aiRoot, name);
                if (Directory.Exists(path) && IsSafeDescendant(aiRoot, path) && IsOldTree(path, olderThan))
                    Add(DeleteOwnedTree(aiRoot, path));
            }
        return new(files, bytes);
        void Add(TempCleanupResult result) { files += result.Files; bytes += result.Bytes; }
    }

    private static TempCleanupResult SweepChildren(string root, DateTime cutoff)
    {
        var result = new TempCleanupResult(0, 0);
        if (!Directory.Exists(root) || IsLink(root) || HasActiveLease(root)) return result;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (!IsSafeDescendant(root, entry)) continue;
            var deleted = Directory.Exists(entry)
                ? IsOldTree(entry, cutoff) && !HasActiveLease(entry) ? DeleteOwnedTree(root, entry) : new(0, 0)
                : File.GetLastWriteTimeUtc(entry) < cutoff ? DeleteFile(root, entry) : new(0, 0);
            result = new(result.Files + deleted.Files, result.Bytes + deleted.Bytes);
        }
        return result;
    }

    public static TempCleanupResult CleanupOldPackages(string packages, string currentVersion, DateTime cutoff)
    {
        var result = new TempCleanupResult(0, 0);
        if (!Directory.Exists(packages) || IsLink(packages)) return result;
        if (!Version.TryParse(currentVersion, out var current)) return result;
        var candidates = new List<(string Path, Version Version, bool Delta)>();
        foreach (var file in Directory.EnumerateFiles(packages, "*.nupkg"))
        {
            try
            {
                var match = Regex.Match(Path.GetFileName(file),
                    @"^Spectralis-(?<version>\d+\.\d+\.\d+(?:\.\d+)?)(?:-(?:win|linux|osx)-(?:x64|x86|arm64))?-(?<kind>full|delta)\.nupkg$", RegexOptions.IgnoreCase);
                if (match.Success && Version.TryParse(match.Groups["version"].Value, out var version) && version < current)
                    candidates.Add((file, version, match.Groups["kind"].Value.Equals("delta", StringComparison.OrdinalIgnoreCase)));
            }
            catch { }
        }
        var rollback = candidates.Where(p => !p.Delta).Select(p => p.Version).Max();
        foreach (var candidate in candidates)
        {
            if (candidate.Version == rollback || File.GetLastWriteTimeUtc(candidate.Path) >= cutoff) continue;
            var deleted = DeleteFile(packages, candidate.Path);
            result = new(result.Files + deleted.Files, result.Bytes + deleted.Bytes);
        }
        return result;
    }

    public static TempCleanupResult DeleteOwnedTree(string root, string path)
    {
        var result = new TempCleanupResult(0, 0);
        if (!Directory.Exists(path) || !IsSafeDescendant(root, path) || HasActiveLease(path)) return result;
        try
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
            {
                if (IsLink(child))
                {
                    // Unlink an owned venv symlink/junction, never traverse its target.
                    if (IsSafeDescendant(root, Path.GetDirectoryName(child)!))
                    {
                        if (Directory.Exists(child)) Directory.Delete(child, recursive: false);
                        else File.Delete(child);
                    }
                    continue;
                }
                if (!IsSafeDescendant(root, child)) continue;
                var deleted = Directory.Exists(child) ? DeleteOwnedTree(root, child) : DeleteFile(root, child);
                result = new(result.Files + deleted.Files, result.Bytes + deleted.Bytes);
            }
            Directory.Delete(path, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }

    private static TempCleanupResult DeleteFile(string root, string path)
    {
        if (!IsSafeDescendant(root, path)) return new(0, 0);
        try
        {
            var bytes = new FileInfo(path).Length;
            // DeleteOnClose makes the exclusive open and delete one operation.
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            return new(1, bytes);
        }
        catch (IOException) { return new(0, 0); }
        catch (UnauthorizedAccessException) { return new(0, 0); }
    }
    private static bool IsOldTree(string path, DateTime cutoff)
    {
        try
        {
            if (IsLink(path) || Directory.GetLastWriteTimeUtc(path) >= cutoff || HasActiveLease(path)) return false;
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
            {
                if (IsLink(child)) return false;
                if (Directory.Exists(child) ? !IsOldTree(child, cutoff) : File.GetLastWriteTimeUtc(child) >= cutoff) return false;
            }
            return true;
        }
        catch { return false; }
    }
    private static bool HasActiveLease(string path)
    {
        var marker = Path.Combine(path, ".active");
        if (!File.Exists(marker)) return false;
        try { using var file = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
        catch { return true; }
    }
    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static bool SamePath(string a, string? b) => b is not null && Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar)
        .Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool IsSafeDescendant(string root, string target)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullTarget = Path.GetFullPath(target);
        if (!fullTarget.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
        try
        {
            for (var current = fullTarget; current.Length >= fullRoot.Length - 1; current = Path.GetDirectoryName(current)!)
                if ((File.Exists(current) || Directory.Exists(current)) && IsLink(current)) return false;
            return true;
        }
        catch { return false; }
    }
}
