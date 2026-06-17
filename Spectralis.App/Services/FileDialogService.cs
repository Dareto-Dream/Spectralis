using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Spectralis.App.Services
{
    public class FileDialogService
    {
        private readonly Window _owner;

        public FileDialogService(Window owner)
        {
            _owner = owner;
        }

        public async Task<IReadOnlyList<string>> OpenAudioFilesAsync()
        {
            var options = new FilePickerOpenOptions
            {
                AllowMultiple = true,
                Title = "Open Audio Files",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio Files")
                    {
                        Patterns = new[] { "*.mp3", "*.flac", "*.ogg", "*.m4a", "*.aac", "*.wav", "*.opus", "*.wv", "*.ape" }
                    },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            };

            var result = await _owner.StorageProvider.OpenFilePickerAsync(options);
            var paths = new List<string>();
            foreach (var f in result)
                if (f.TryGetLocalPath() is string local) paths.Add(local);
            return paths;
        }

        public async Task<string?> OpenFolderAsync()
        {
            var options = new FolderPickerOpenOptions { Title = "Select Music Folder" };
            var result = await _owner.StorageProvider.OpenFolderPickerAsync(options);
            if (result.Count == 0) return null;
            return result[0].TryGetLocalPath();
        }

        public async Task<string?> SaveM3UAsync()
        {
            var options = new FilePickerSaveOptions
            {
                Title = "Export Playlist",
                DefaultExtension = ".m3u",
                FileTypeChoices = new[] { new FilePickerFileType("M3U Playlist") { Patterns = new[] { "*.m3u" } } }
            };
            var result = await _owner.StorageProvider.SaveFilePickerAsync(options);
            return result?.TryGetLocalPath();
        }
    }
}
