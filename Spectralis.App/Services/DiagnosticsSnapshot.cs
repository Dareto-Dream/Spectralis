using System.Reflection;
using System.Runtime.InteropServices;

namespace Spectralis.App.Services;

public static class DiagnosticsSnapshot
{
    public static string CurrentVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(DiagnosticsSnapshot).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            return string.IsNullOrWhiteSpace(informational)
                ? assembly.GetName().Version?.ToString() ?? "unknown"
                : informational.Split('+')[0].Trim();
        }
    }

    public static string Build()
    {
        var processPath = Environment.ProcessPath ?? string.Empty;
        var installFolder = string.IsNullOrWhiteSpace(processPath)
            ? string.Empty
            : Path.GetDirectoryName(processPath) ?? string.Empty;

        return string.Join(
            Environment.NewLine,
            $"Spectralis {CurrentVersion}",
            $"Runtime: {RuntimeInformation.FrameworkDescription}",
            $"OS: {RuntimeInformation.OSDescription}",
            $"Architecture: {RuntimeInformation.ProcessArchitecture}",
            $"Executable: {processPath}",
            $"Install folder: {installFolder}",
            $"Settings: {AppSettingsStore.SettingsPath}",
            $"Logs: {AppLogPaths.LogDirectory}");
    }
}
