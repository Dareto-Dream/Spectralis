using System;
using Spectralis.Core.Audio;
using Spectralis.Core.Infrastructure;
using Spectralis.Core.Library;
using Spectralis.Core.Playlists;
using Spectralis.Core.Queue;
using Spectralis.Core.Settings;
using Spectralis.Core.Streaming;
using Spectralis.Core.Tags;

namespace Spectralis.App.Services
{
    public class ServiceContainer : IDisposable
    {
        public IAudioEngine AudioEngine { get; }
        public AudioPipeline Pipeline { get; }
        public LibraryManager Library { get; }
        public PlayQueue Queue { get; }
        public QueueAutoAdvance AutoAdvance { get; }
        public StreamingRegistry Streaming { get; }
        public ISpectralLogger Logger { get; }
        public PlaylistManager Playlists { get; }
        public SettingsRepository Settings { get; }
        public TagEditorService TagEditor { get; }
        public CoverArtStore CoverArt { get; }

        private bool _disposed;

        public ServiceContainer()
        {
            Logger = new FileLogger(System.IO.Path.Combine(AppPaths.LogsDirectory, "spectralis.log"));

            Settings = new SettingsRepository(AppPaths.SettingsFilePath);

            var opts = new AudioEngineOptions();
            AudioEngine = AudioEngineFactory.Create(opts);
            Pipeline = new AudioPipeline(opts.FftSize, opts.SpectrumBands);

            Library = new LibraryManager(AppPaths.LibraryDbPath);
            Queue = new PlayQueue();
            AutoAdvance = new QueueAutoAdvance(Queue, AudioEngine);
            Streaming = new StreamingRegistry();

            Playlists = new PlaylistManager(Library.Db);
            TagEditor = new TagEditorService();
            CoverArt = new CoverArtStore(AppPaths.CoverArtCacheDirectory);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            AutoAdvance.Dispose();
            AudioEngine.Dispose();
            Pipeline.Dispose();
            Library.Dispose();
            Streaming.Dispose();
            Playlists.Dispose();
        }
    }
}
