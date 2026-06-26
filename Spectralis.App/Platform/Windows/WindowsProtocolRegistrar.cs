using System.Runtime.Versioning;
using Microsoft.Win32;
using Spectralis.Core.Platform;

namespace Spectralis.App.Platform.Windows;

/// <summary>
/// Current-user registration for the spectralis:// protocol and audio file
/// associations - the HKCU subset of the WinForms DefaultAppRegistrar, pointed
/// at the Avalonia executable.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProtocolRegistrar : IProtocolRegistrar
{
    private const string AppDisplayName = "Spectralis";
    private const string ProgId = "Spectralis.AudioFile";
    private const string Protocol = "spectralis";
    private const string CapabilitiesPath = @"Software\Spectralis\Capabilities";
    private const string AppDescription =
        "Desktop audio player with a live visualizer, drag-and-drop support, and broad Windows codec compatibility.";

    private static string ExecutablePath =>
        Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable.");

    internal static string BuildOpenCommand(string executablePath) => $"\"{executablePath}\" \"%1\"";

    public bool IsProtocolRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Protocol}\shell\open\command");
            return string.Equals(
                key?.GetValue(string.Empty) as string,
                BuildOpenCommand(ExecutablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void RegisterProtocol()
    {
        using var protocolKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Protocol}");
        protocolKey.SetValue(string.Empty, $"URL:{AppDisplayName} Protocol");
        protocolKey.SetValue("URL Protocol", string.Empty);

        using var iconKey = protocolKey.CreateSubKey("DefaultIcon");
        iconKey.SetValue(string.Empty, $"\"{ExecutablePath}\",0");

        using var commandKey = protocolKey.CreateSubKey(@"shell\open\command");
        commandKey.SetValue(string.Empty, BuildOpenCommand(ExecutablePath));
    }

    public bool AreFileAssociationsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
            return string.Equals(
                key?.GetValue(string.Empty) as string,
                BuildOpenCommand(ExecutablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void RegisterFileAssociations(IReadOnlyList<string> extensions)
    {
        var openCommand = BuildOpenCommand(ExecutablePath);

        // ProgId
        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progIdKey.SetValue(string.Empty, $"{AppDisplayName} Audio File");

            using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
            iconKey.SetValue(string.Empty, $"\"{ExecutablePath}\",0");

            using var commandKey = progIdKey.CreateSubKey(@"shell\open\command");
            commandKey.SetValue(string.Empty, openCommand);
        }

        // Extension to ProgId ("Open with" visibility; UserChoice still wins when set)
        foreach (var extension in extensions)
        {
            using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}");
            extKey.SetValue(string.Empty, ProgId);

            using var openWithKey = extKey.CreateSubKey("OpenWithProgIds");
            openWithKey.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        // Capabilities + RegisteredApplications so the app appears in Default Apps
        using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
        {
            capabilities.SetValue("ApplicationName", AppDisplayName);
            capabilities.SetValue("ApplicationDescription", AppDescription);

            using var fileAssociations = capabilities.CreateSubKey("FileAssociations");
            foreach (var extension in extensions)
            {
                fileAssociations.SetValue(extension, ProgId);
            }

            using var urlAssociations = capabilities.CreateSubKey("URLAssociations");
            urlAssociations.SetValue(Protocol, Protocol);
        }

        using var registeredApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications");
        registeredApps.SetValue(AppDisplayName, CapabilitiesPath);
    }
}
