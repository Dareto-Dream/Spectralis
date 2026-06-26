using System.IO;

namespace Spectralis;

public static class AppUpdateNoticeStore
{
    private static string NoticePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "pending-update-notice.txt");

    public static void MarkPending(string? version)
    {
        try
        {
            var directory = Path.GetDirectoryName(NoticePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(NoticePath, string.IsNullOrWhiteSpace(version) ? "" : version.Trim());
        }
        catch
        {
            // Update notices should never interrupt Squirrel install/update events.
        }
    }

    public static string? ConsumePending()
    {
        try
        {
            if (!File.Exists(NoticePath))
                return null;

            var version = File.ReadAllText(NoticePath).Trim();
            File.Delete(NoticePath);
            return version;
        }
        catch
        {
            return null;
        }
    }
}
