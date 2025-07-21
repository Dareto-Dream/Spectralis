using System;
using Spectralis.Core.Audio;
using Spectralis.Core.Infrastructure;
using Spectralis.Core.Library;
using Spectralis.Core.Queue;
using Spectralis.Core.Streaming;

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

        private bool _disposed;

        public ServiceContainer()
        {
            Logger = new FileLogger(System.IO.Path.Combine(AppPaths.LogsDirectory, "spectralis.log"));

            var opts = new AudioEngineOptions();
            AudioEngine = AudioEngineFactory.Create(opts);
            Pipeline = new AudioPipeline(opts.FftSize, opts.SpectrumBands);

            Library = new LibraryManager(AppPaths.LibraryDbPath);
            Queue = new PlayQueue();
            AutoAdvance = new QueueAutoAdvance(Queue, AudioEngine);
            Streaming = new StreamingRegistry();
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
        }
    }
}
