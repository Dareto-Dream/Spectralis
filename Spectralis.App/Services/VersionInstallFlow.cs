using Spectralis.Core.Platform;

namespace Spectralis.App.Services;

public static class VersionInstallFlow
{
    public static async Task RunAsync(string version, string currentVersion, Func<Task<bool>> confirmDowngrade,
        Action prepareRecovery, Func<Action, Task> install, Action readyToRestart)
    {
        if (ReleaseVersion.Compare(version, currentVersion) < 0)
        {
            if (!await confirmDowngrade()) return;
            prepareRecovery();
        }
        // The installer calls this only after a successful, uncancelled download. Preferences
        // must not change merely because the user accepted the warning or started a download.
        await install(readyToRestart);
    }
}
