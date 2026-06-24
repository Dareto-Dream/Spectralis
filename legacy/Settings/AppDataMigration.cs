using System.IO;

namespace Spectralis;

public static class AppDataMigration
{
    private const string CurrentFolderName = "Spectralis";
    private static readonly string[] LegacyFolderNames =
    [
        "Spectrallis",
        "AudioPlayer"
    ];

    public static void MigrateLegacyFolder()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                return;

            var currentPath = Path.Combine(localAppData, CurrentFolderName);
            foreach (var legacyFolderName in LegacyFolderNames)
            {
                var legacyPath = Path.Combine(localAppData, legacyFolderName);
                if (Directory.Exists(legacyPath))
                    CopyMissingDirectoryContents(legacyPath, currentPath);
            }
        }
        catch
        {
        }
    }

    private static void CopyMissingDirectoryContents(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var sourceFile in Directory.EnumerateFiles(sourcePath))
        {
            var destinationFile = Path.Combine(destinationPath, Path.GetFileName(sourceFile));
            if (!File.Exists(destinationFile))
                File.Copy(sourceFile, destinationFile);
        }

        foreach (var sourceDirectory in Directory.EnumerateDirectories(sourcePath))
        {
            var destinationDirectory = Path.Combine(destinationPath, Path.GetFileName(sourceDirectory));
            CopyMissingDirectoryContents(sourceDirectory, destinationDirectory);
        }
    }
}
