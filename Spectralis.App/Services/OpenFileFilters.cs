using Avalonia.Platform.Storage;
using Spectralis.Core.Common;

namespace Spectralis.App.Services;

internal static class OpenFileFilters
{
    public static IReadOnlyList<FilePickerFileType> Build(bool audioOnly = false)
    {
        var audioPatterns = SupportedAudioFormats.Extensions.Select(ext => "*" + ext).ToArray();
        string[] packagePatterns = ["*.spectralis", "*.spectral"];
        var filters = new List<FilePickerFileType>();
        if (!audioOnly)
        {
            filters.Add(new FilePickerFileType("Audio, capsules and worlds")
            {
                Patterns = [.. audioPatterns, .. packagePatterns],
            });
        }
        filters.Add(new FilePickerFileType("Audio files") { Patterns = audioPatterns });
        if (!audioOnly)
        {
            filters.Add(new FilePickerFileType("Spectralis capsules and worlds")
            {
                Patterns = packagePatterns,
            });
        }
        filters.Add(FilePickerFileTypes.All);
        return filters;
    }
}
