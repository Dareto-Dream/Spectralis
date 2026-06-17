using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using Spectralis.Core.Audio.FormatReaders;
using Spectralis.Core.Models;

namespace Spectralis.Core.Audio
{
    public class NaudioAudioEngine : IAudioEngine
    {
        private readonly AudioEngineOptions _options;
        private readonly FormatReaderRegistry _readers;
        private readonly AudioPipeline _pipeline;
        private IWavePlayer? _waveOut;
        private IAudioReader? _reader;
        private float _volume;
        private bool _disposed;
        private readonly object _readerLock = new object();

        public event EventHandler? PlaybackStarted;
        public event EventHandler? PlaybackPaused;
        public event EventHandler? PlaybackStopped;
        public event EventHandler? TrackEnded;
        public event EventHandler<TrackInfo>? TrackLoaded;

        public TrackInfo? CurrentTrack { get; private set; }
        public PlaybackState State { get; private set; } = PlaybackState.Stopped;
        public bool IsPlaying => State == PlaybackState.Playing;
        public bool IsPaused => State == PlaybackState.Paused;

        public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0f, 1f);
                if (_waveOut != null) _waveOut.Volume = _volume;
            }
        }

        public NaudioAudioEngine(AudioEngineOptions? options = null)
        {
            _options = options ?? new AudioEngineOptions();
            _readers = FormatReaderRegistry.CreateDefault();
            _pipeline = new AudioPipeline();
            _volume = _options.InitialVolume;
        }

        public Task LoadAsync(string path)
        {
            return Task.Run(() =>
            {
                Stop();
                var newReader = _readers.Create(path)
                    ?? throw new NotSupportedException($"No reader for {Path.GetExtension(path)}");

                lock (_readerLock)
                {
                    _reader?.Dispose();
                    _reader = newReader;
                    _waveOut?.Dispose();
                    _waveOut = null;
                }

                CurrentTrack = new TrackInfo
                {
                    FilePath = path,
                    Title = Path.GetFileNameWithoutExtension(path),
                    Duration = newReader.TotalTime
                };

                TrackLoaded?.Invoke(this, CurrentTrack);
            });
        }

        public Task LoadStreamAsync(string url, string? mimeHint = null)
        {
            throw new NotImplementedException("Stream loading not yet implemented in NaudioAudioEngine");
        }

        public void Play()
        {
            if (_reader == null) return;
            if (_waveOut == null) SetupOutput();

            _waveOut!.Play();
            State = PlaybackState.Playing;
            _pipeline.Start();
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            _waveOut?.Pause();
            State = PlaybackState.Paused;
            _pipeline.Stop();
            PlaybackPaused?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            _waveOut?.Stop();
            State = PlaybackState.Stopped;
            _pipeline.Stop();
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Seek(TimeSpan position)
        {
            lock (_readerLock)
            {
                if (_reader != null)
                    _reader.CurrentTime = position;
            }
        }

        private void SetupOutput()
        {
            _waveOut?.Dispose();
            _waveOut = new WaveOutEvent { DesiredLatency = _options.DesiredLatencyMs, DeviceNumber = _options.WaveOutDeviceNumber };
            _waveOut.Volume = _volume;

            var provider = _reader!.AsWaveProvider().ToSampleProvider();
            var tap = _pipeline.CreateTap(provider);
            _waveOut.Init(tap.ToWaveProvider());
            _waveOut.PlaybackStopped += OnPlaybackStopped;
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (State == PlaybackState.Playing)
            {
                State = PlaybackState.Stopped;
                TrackEnded?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _waveOut?.Dispose();
            _reader?.Dispose();
            _pipeline.Dispose();
        }
    }
}
