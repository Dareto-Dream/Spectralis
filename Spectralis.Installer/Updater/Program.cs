using System.Diagnostics;
using System.Text.Json;

namespace Spectralis.Updater;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--version"]) { Console.WriteLine("Update Spectralis 1.0"); return 0; }
        var checkOnly = args is ["--check"];
        if (args.Length > 0 && !checkOnly) { Console.Error.WriteLine("Usage: UpdateSpectralis [--check|--version]"); return 1; }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        string? temporaryDirectory = null;
        string? stagedImage = null;
        try
        {
            Console.WriteLine("Update Spectralis\n");
            var rid = OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsLinux() ? "linux-x64" :
                OperatingSystem.IsMacOS() ? "osx-x64" : throw new PlatformNotSupportedException();
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(12) };
            var client = new RecoveryClient(http);
            var release = await client.FindLatestAsync(rid, cancellation.Token);
            Console.WriteLine($"Latest stable release: {release.Version}");
            if (checkOnly) { Console.WriteLine(release.Installer); return 0; }
            Console.WriteLine("Close Spectralis before continuing. Your music and settings will be kept.");
            Console.Write("Download and install this release? [y/N] ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase)) return 2;

            string? imagePath = null;
            if (OperatingSystem.IsLinux())
            {
                var configPath = Path.Combine(AppContext.BaseDirectory, "target.json");
                if (File.Exists(configPath))
                {
                    using var config = JsonDocument.Parse(File.ReadAllText(configPath));
                    if (config.RootElement.TryGetProperty("appImagePath", out var value)) imagePath = value.GetString();
                }
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    Console.Write("Path to the Spectralis AppImage to replace: ");
                    imagePath = Console.ReadLine()?.Trim().Trim('"');
                }
                if (string.IsNullOrWhiteSpace(imagePath) || !Path.IsPathFullyQualified(imagePath) ||
                    !imagePath.EndsWith(".AppImage", StringComparison.Ordinal) || !File.Exists(imagePath))
                    throw new InvalidOperationException("Choose the existing Spectralis AppImage and try again.");
            }
            temporaryDirectory = Path.Combine(Path.GetTempPath(), "spectralis-updater-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            var download = Path.Combine(temporaryDirectory, rid == "win-x64" ? "Spectralis-Setup.exe" :
                rid == "osx-x64" ? "Spectralis-Setup.pkg" : "Spectralis.AppImage");
            var lastProgress = Stopwatch.StartNew();
            var progress = new Progress<long>(bytes =>
            {
                if (lastProgress.ElapsedMilliseconds < 1000) return;
                Console.Write($"\rDownloading: {bytes * 100 / release.Size}%   ");
                lastProgress.Restart();
            });
            await client.DownloadAsync(release, download, progress, cancellation.Token);
            Console.WriteLine("\rDownload verified.                     ");
            cancellation.Token.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows())
            {
                var installer = Process.Start(new ProcessStartInfo(download) { UseShellExecute = true })
                    ?? throw new InvalidOperationException("The installer could not start.");
                await installer.WaitForExitAsync();
                if (installer.ExitCode != 0) throw new InvalidOperationException("The installer did not finish. You can run this updater again.");
            }
            else if (OperatingSystem.IsMacOS())
            {
                using var installer = Process.Start(new ProcessStartInfo("/usr/bin/open")
                    { UseShellExecute = false, ArgumentList = { "-W", download } })
                    ?? throw new InvalidOperationException("The installer could not start.");
                await installer.WaitForExitAsync();
                if (installer.ExitCode != 0) throw new InvalidOperationException("The installer did not finish. You can run this updater again.");
            }
            else if (OperatingSystem.IsLinux())
            {
                // Stage beside the target so replacement stays on one filesystem. The mounted
                // AppImage's files are never edited; a failed download leaves the old image intact.
                stagedImage = imagePath + ".update-" + Guid.NewGuid().ToString("N");
                File.Copy(download, stagedImage, overwrite: false);
                File.SetUnixFileMode(stagedImage, File.GetUnixFileMode(imagePath!) | UnixFileMode.UserExecute);
                File.Move(stagedImage, imagePath!, overwrite: true);
                stagedImage = null;
            }
            Console.WriteLine($"Spectralis {release.Version} is ready. Open Spectralis to continue.\nAutomatic updates can be enabled again in Settings.");
            return 0;
        }
        catch (OperationCanceledException) { Console.WriteLine("\nUpdate canceled. Run this updater whenever you are ready."); return 2; }
        catch (Exception ex) { Console.Error.WriteLine("\n" + ex.Message); return 1; }
        finally
        {
            if (stagedImage is not null) try { File.Delete(stagedImage); } catch { }
            if (temporaryDirectory is not null) try { Directory.Delete(temporaryDirectory, recursive: true); } catch { }
            if (!checkOnly && !Console.IsInputRedirected) { Console.WriteLine("\nPress Enter to close."); Console.ReadLine(); }
        }
    }
}
