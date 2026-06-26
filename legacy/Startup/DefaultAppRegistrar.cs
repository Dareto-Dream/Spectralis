using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Spectralis;

public static class DefaultAppRegistrar
{
    private const string AppDisplayName = "Spectralis";
    private const string AppExecutableName = "Spectralis.exe";
    private const string LegacyAppDisplayName = "Audio Player";
    private const string LegacyCompactAppDisplayName = "AudioPlayer";
    private const string AppCapabilitiesPath = @"Software\Spectralis\Capabilities";
    private const string LegacyAppCapabilitiesPath = @"Software\AudioPlayer\Capabilities";
    private const string ProgId = "Spectralis.AudioFile";
    private const string LegacyProgId = "AudioPlayer.AudioFile";
    private const string LegacyExecutableName = "AudioPlayer.exe";
    private const string SquirrelUpdateExecutableName = "Update.exe";
    private const string SharedPlayProtocol = "spectralis";
    private const string AppDescription =
        "Desktop audio player with a live visualizer, drag-and-drop support, and broad Windows codec compatibility.";

    public static void RegisterCurrentUser()
    {
        var executablePath = Application.ExecutablePath;
        var executableName = Path.GetFileName(executablePath);
        var openCommand = BuildOpenCommand(executablePath);

        RegisterSharedPlayProtocolCurrentUser();

        RegisterApplicationEntry(executableName, AppDisplayName, openCommand);
        if (!string.Equals(executableName, LegacyExecutableName, StringComparison.OrdinalIgnoreCase))
            RegisterApplicationEntry(LegacyExecutableName, AppDisplayName, openCommand);

        RegisterProgId(ProgId, executablePath, openCommand);
        RegisterProgId(LegacyProgId, executablePath, openCommand);

        // Extension to ProgId class entries (the direct association layer).
        // UserChoice takes priority when Windows has a hash-verified user selection, but
        // these entries act as the canonical fallback and make the app visible in "Open with".
        foreach (var extension in SupportedAudioFormats.Extensions)
        {
            using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}");
            extKey?.SetValue(string.Empty, ProgId);

            using var openWithKey = extKey?.CreateSubKey("OpenWithProgIds");
            openWithKey?.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            openWithKey?.SetValue(LegacyProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        RegisterCapabilities(AppCapabilitiesPath, ProgId);
        RegisterCapabilities(LegacyAppCapabilitiesPath, LegacyProgId);

        using var registeredApplicationsKey = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications");
        registeredApplicationsKey?.DeleteValue(LegacyAppDisplayName, throwOnMissingValue: false);
        registeredApplicationsKey?.DeleteValue(LegacyCompactAppDisplayName, throwOnMissingValue: false);
        registeredApplicationsKey?.SetValue(AppDisplayName, AppCapabilitiesPath);
    }

    public static bool IsCurrentUserRegistrationCurrent()
    {
        try
        {
            var executablePath = Application.ExecutablePath;
            var executableName = Path.GetFileName(executablePath);
            var openCommand = BuildOpenCommand(executablePath);

            return IsCommandRegistered(
                    $@"Software\Classes\Applications\{executableName}\shell\open\command",
                    openCommand) &&
                IsCommandRegistered(
                    $@"Software\Classes\{ProgId}\shell\open\command",
                    openCommand) &&
                IsRegisteredApplicationPath(AppDisplayName, AppCapabilitiesPath) &&
                IsSharedPlayProtocolRegisteredForCurrentUser() &&
                !LegacyRegistrationNeedsMigration(openCommand);
        }
        catch
        {
            return false;
        }
    }

    public static bool ShouldRefreshCurrentUserRegistration()
    {
        try
        {
            var executablePath = Application.ExecutablePath;
            var executableName = Path.GetFileName(executablePath);
            var openCommand = BuildOpenCommand(executablePath);
            var currentRegistrationExists = RegistryValueExists(@"Software\RegisteredApplications", AppDisplayName);

            return (currentRegistrationExists && !IsCurrentUserRegistrationCurrent()) ||
                CommandExistsButDiffers(
                    $@"Software\Classes\Applications\{executableName}\shell\open\command",
                    openCommand) ||
                CommandExistsButDiffers(
                    $@"Software\Classes\{ProgId}\shell\open\command",
                    openCommand) ||
                LegacyRegistrationNeedsMigration(openCommand);
        }
        catch
        {
            return true;
        }
    }

    public static void RegisterSharedPlayProtocolCurrentUser()
    {
        var executablePath = Application.ExecutablePath;
        var openCommand = BuildOpenCommand(executablePath);

        using var protocolKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SharedPlayProtocol}");
        protocolKey?.SetValue(string.Empty, "URL:Spectralis Shared Play");
        protocolKey?.SetValue("URL Protocol", string.Empty);

        using (var defaultIconKey = protocolKey?.CreateSubKey("DefaultIcon"))
        {
            defaultIconKey?.SetValue(string.Empty, $"\"{executablePath}\",0");
        }

        using (var commandKey = protocolKey?.CreateSubKey(@"shell\open\command"))
        {
            commandKey?.SetValue(string.Empty, openCommand);
        }
    }

    public static bool IsSharedPlayProtocolRegisteredForCurrentUser()
    {
        var executablePath = Application.ExecutablePath;
        var expectedOpenCommand = BuildOpenCommand(executablePath);

        return IsCommandRegistered(
            $@"Software\Classes\{SharedPlayProtocol}\shell\open\command",
            expectedOpenCommand);
    }

    public static void OpenDefaultAppsSettings()
    {
        // registeredAppUser deep-links directly to the Spectralis entry on Windows 11 22H2+
        var deepLink = $"ms-settings:defaultapps?registeredAppUser={AppDisplayName}";
        try
        {
            Process.Start(new ProcessStartInfo { FileName = deepLink, UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo { FileName = "ms-settings:defaultapps", UseShellExecute = true });
        }
    }

    private static void RegisterApplicationEntry(string executableName, string displayName, string openCommand)
    {
        using var applicationKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{executableName}");
        applicationKey?.SetValue("FriendlyAppName", displayName);

        using (var commandKey = applicationKey?.CreateSubKey(@"shell\open\command"))
        {
            commandKey?.SetValue(string.Empty, openCommand);
        }

        using (var supportedTypesKey = applicationKey?.CreateSubKey("SupportedTypes"))
        {
            foreach (var extension in SupportedAudioFormats.Extensions)
            {
                supportedTypesKey?.SetValue(extension, string.Empty);
            }
        }
    }

    private static void RegisterProgId(string progId, string executablePath, string openCommand)
    {
        using var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}");
        progIdKey?.SetValue(string.Empty, $"{AppDisplayName} Audio File");

        using (var defaultIconKey = progIdKey?.CreateSubKey("DefaultIcon"))
        {
            defaultIconKey?.SetValue(string.Empty, $"\"{executablePath}\",0");
        }

        using (var commandKey = progIdKey?.CreateSubKey(@"shell\open\command"))
        {
            commandKey?.SetValue(string.Empty, openCommand);
        }
    }

    private static void RegisterCapabilities(string capabilitiesPath, string progId)
    {
        using var capabilitiesKey = Registry.CurrentUser.CreateSubKey(capabilitiesPath);
        capabilitiesKey?.SetValue("ApplicationName", AppDisplayName);
        capabilitiesKey?.SetValue("ApplicationDescription", AppDescription);

        using var fileAssociationsKey = capabilitiesKey?.CreateSubKey("FileAssociations");
        foreach (var extension in SupportedAudioFormats.Extensions)
        {
            fileAssociationsKey?.SetValue(extension, progId);
        }
    }

    private static bool LegacyRegistrationNeedsMigration(string openCommand)
    {
        return CommandExistsButDiffers(
                $@"Software\Classes\Applications\{LegacyExecutableName}\shell\open\command",
                openCommand) ||
            CommandExistsButDiffers(
                $@"Software\Classes\{LegacyProgId}\shell\open\command",
                openCommand) ||
            RegistryValueExists(@"Software\RegisteredApplications", LegacyAppDisplayName) ||
            RegistryValueExists(@"Software\RegisteredApplications", LegacyCompactAppDisplayName) ||
            LegacyUserChoiceNeedsMigration(openCommand);
    }

    private static bool LegacyUserChoiceNeedsMigration(string openCommand)
    {
        foreach (var extension in SupportedAudioFormats.Extensions)
        {
            using var userChoiceKey = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice",
                writable: false);

            var progId = userChoiceKey?.GetValue("ProgId") as string;
            if (string.Equals(progId, LegacyProgId, StringComparison.OrdinalIgnoreCase) &&
                !IsCommandRegistered($@"Software\Classes\{LegacyProgId}\shell\open\command", openCommand))
            {
                return true;
            }

            if (string.Equals(progId, $@"Applications\{LegacyExecutableName}", StringComparison.OrdinalIgnoreCase) &&
                !IsCommandRegistered(
                    $@"Software\Classes\Applications\{LegacyExecutableName}\shell\open\command",
                    openCommand))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRegisteredApplicationPath(string displayName, string capabilitiesPath)
    {
        using var registeredApplicationsKey = Registry.CurrentUser.OpenSubKey(
            @"Software\RegisteredApplications",
            writable: false);

        var actualPath = registeredApplicationsKey?.GetValue(displayName) as string;
        return string.Equals(actualPath, capabilitiesPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCommandRegistered(string registryPath, string expectedCommand)
    {
        using var commandKey = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
        var actualCommand = commandKey?.GetValue(string.Empty) as string;
        return string.Equals(actualCommand, expectedCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CommandExistsButDiffers(string registryPath, string expectedCommand)
    {
        using var commandKey = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
        var actualCommand = commandKey?.GetValue(string.Empty) as string;
        return actualCommand is not null &&
            !string.Equals(actualCommand, expectedCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RegistryValueExists(string registryPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: false);
        return key?.GetValue(valueName) is not null;
    }

    private static string BuildOpenCommand(string executablePath)
    {
        var squirrelUpdatePath = GetInstalledSquirrelUpdatePath();
        return squirrelUpdatePath is not null
            ? BuildSquirrelOpenCommand(squirrelUpdatePath)
            : $"\"{executablePath}\" \"%1\"";
    }

    private static string? GetInstalledSquirrelUpdatePath()
    {
        var updatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDisplayName,
            SquirrelUpdateExecutableName);

        return File.Exists(updatePath) ? updatePath : null;
    }

    private static string BuildSquirrelOpenCommand(string updatePath) =>
        $"\"{updatePath}\" --processStart \"{AppExecutableName}\" --process-start-args \"\\\"%1\\\"\"";
}
