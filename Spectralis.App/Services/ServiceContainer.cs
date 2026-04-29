using System;
using Spectralis.App.Visualizers;
using Spectralis.Core.AlbumWorld;
using Spectralis.Core.Analysis;
using Spectralis.Core.Audio;
using Spectralis.Core.Capsule;
using Spectralis.Core.Infrastructure;
using Spectralis.Core.Library;
using Spectralis.Core.Lyrics;
using Spectralis.Core.Playlists;
using Spectralis.Core.Queue;
using Spectralis.Core.Settings;
using Spectralis.Core.Streaming;
using Spectralis.Core.Tags;
using Spectralis.Core.Timeline;
using Spectralis.Core.Visualizers;

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
        public VisualizerRegistry VisualizerRegistry { get; }
        public VisualizerScriptManager VisualizerScripts { get; }
        public AnalysisWorker Analysis { get; }
        public AnalysisCache AnalysisCache { get; }
        public CapsuleReader CapsuleReader { get; }
        public QueuePersistence QueuePersistence { get; }
        public QueueService QueueService { get; }
        public LyricsAnnotationStore LyricsAnnotations { get; }
        public LyricsLoader LyricsLoader { get; }
        public LyricsService Lyrics { get; }
        public CapsuleTrustStore CapsuleTrust { get; }
        public CapsuleCache CapsuleCache { get; }
        public AlbumWorldService AlbumWorld { get; }
        public TimelineService Timeline { get; }

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
            VisualizerRegistry = new VisualizerRegistry();
            VisualizerRegistration.RegisterAll(VisualizerRegistry);
            VisualizerScripts = new VisualizerScriptManager(AppPaths.VisualizerScriptsDirectory);
            Analysis = new AnalysisWorker();
            AnalysisCache = new AnalysisCache(AppPaths.AnalysisCachePath);
            CapsuleReader = new CapsuleReader();
            QueuePersistence = new QueuePersistence(AppPaths.QueueSnapshotPath);
            QueueService = new QueueService(Queue, QueuePersistence);
            LyricsAnnotations = new LyricsAnnotationStore();
            LyricsLoader = new LyricsLoader();
            Lyrics = new LyricsService();
            CapsuleTrust = new CapsuleTrustStore(AppPaths.CapsuleTrustStorePath);
            CapsuleCache = new CapsuleCache(AppPaths.CapsuleCacheDirectory);
            AlbumWorld = new AlbumWorldService(AppPaths.AlbumWorldCacheDirectory);
            Timeline = new TimelineService();
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
            QueueService.Dispose();
            Lyrics.Dispose();
            Timeline.Dispose();
            AlbumWorld.Dispose();
        }
    }
}
