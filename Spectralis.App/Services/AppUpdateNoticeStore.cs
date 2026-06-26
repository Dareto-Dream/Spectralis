using System.Text.Json;

namespace Spectralis.App.Services;

public static class AppUpdateNoticeStore
{
    private sealed class UpdateNotice
    {
        public string PendingVersion { get; set; } = string.Empty;
    }

    private static string NoticePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spectralis",
            "pending-update.json");

    public static void SavePending(string version)
    {
        var directory = Path.GetDirectoryName(NoticePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(NoticePath, JsonSerializer.Serialize(new UpdateNotice { PendingVersion = version }));
    }

    public static string? ConsumePending()
    {
        try
        {
            if (!File.Exists(NoticePath))
            {
                return null;
            }

            var notice = JsonSerializer.Deserialize<UpdateNotice>(File.ReadAllText(NoticePath));
            File.Delete(NoticePath);
            return string.IsNullOrWhiteSpace(notice?.PendingVersion) ? null : notice.PendingVersion.Trim();
        }
        catch
        {
            return null;
        }
    }
}
