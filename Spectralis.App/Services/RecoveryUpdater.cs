using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace Spectralis.App.Services;

public static class RecoveryUpdater
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectralis", "Updater");
    public static string FileName => OperatingSystem.IsWindows() ? "UpdateSpectralis.exe" : "UpdateSpectralis";

    public static string EnsureInstalled(string? directory = null, string? sourceDirectory = null, bool createShortcut = true)
    {
        directory ??= DirectoryPath;
        sourceDirectory ??= Path.Combine(AppContext.BaseDirectory, "Updater");
        var source = Path.Combine(sourceDirectory, FileName);
        var target = Path.Combine(directory, FileName);
        if (!File.Exists(source)) throw new FileNotFoundException("The recovery updater is missing. Reinstall the latest Spectralis release.");
        Directory.CreateDirectory(directory);
        if (!File.Exists(target) || !SameFile(source, target))
        {
            var staged = target + "." + Guid.NewGuid().ToString("N");
            try { File.Copy(source, staged); File.Move(staged, target, overwrite: true); }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(target, File.GetUnixFileMode(target) | UnixFileMode.UserExecute);
            var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            if (!string.IsNullOrWhiteSpace(appImage) && Path.IsPathFullyQualified(appImage) && File.Exists(appImage))
                File.WriteAllText(Path.Combine(directory, "target.json"), JsonSerializer.Serialize(new { appImagePath = appImage }));
        }
        if (createShortcut)
        {
            if (OperatingSystem.IsWindows()) CreateWindowsShortcut(target);
            else if (OperatingSystem.IsLinux())
            {
                var applications = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "applications");
                Directory.CreateDirectory(applications);
                var executable = target.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
                File.WriteAllText(Path.Combine(applications, "spectralis-updater.desktop"),
                    $"[Desktop Entry]\nType=Application\nName=Update Spectralis\nComment=Return to the latest stable release\nExec=\"{executable}\"\nTerminal=true\nCategories=AudioVideo;Audio;\n");
            }
        }
        return target;
    }

    public static void ShowFolder()
    {
        EnsureInstalled();
        Process.Start(new ProcessStartInfo(DirectoryPath) { UseShellExecute = true });
    }

    private static bool SameFile(string a, string b)
    {
        using var left = File.OpenRead(a);
        using var right = File.OpenRead(b);
        return SHA256.HashData(left).AsSpan().SequenceEqual(SHA256.HashData(right));
    }

    [SupportedOSPlatform("windows")]
    private static void CreateWindowsShortcut(string target)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Spectralis");
        Directory.CreateDirectory(folder);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(Path.Combine(folder, "Update Spectralis.lnk"));
            try
            {
                shortcut.TargetPath = target;
                shortcut.WorkingDirectory = Path.GetDirectoryName(target);
                shortcut.Description = "Return to the latest stable Spectralis release";
                shortcut.Save();
            }
            finally { Marshal.FinalReleaseComObject(shortcut); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
}
