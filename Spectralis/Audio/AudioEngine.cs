using System;
using System.Threading;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace Spectralis.Audio
{
    public class AudioEngine : IDisposable
    {
        private IWavePlayer _waveOut;
        private IAudioReader _reader;
        private WaveChannel32 _waveProvider;
        private readonly object _audioLock = new object();
        private PlaybackState _state = PlaybackState.Stopped;
        private float _volume = 0.8f;
        private Timer _positionTimer;
        private readonly AudioEngineConfig _config;

        public event EventHandler<PlaybackState> StateChanged;
        public event EventHandler<TimeSpan> PositionChanged;
        public event EventHandler TrackEnded;
        public event EventHandler<Exception> PlaybackError;

        public PlaybackState State => _state;
        public Playlist Playlist { get; } = new Playlist();

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Max(0f, Math.Min(1f, value));
                if (_waveProvider != null)
                    _waveProvider.Volume = _volume;
            }
        }

        public TimeSpan Position
        {
            get => _reader?.Position ?? TimeSpan.Zero;
            set
            {
                if (_reader == null) return;
                var clamped = TimeSpan.FromSeconds(Math.Max(0, Math.Min(value.TotalSeconds, Duration.TotalSeconds - 0.1)));
                _reader.Position = clamped;
            }
        }

        public TimeSpan Duration => _reader?.Duration ?? TimeSpan.Zero;

        public AudioPosition AudioPosition => new AudioPosition(Position, Duration);

        public TrackInfo CurrentTrack { get; private set; }

        public AudioEngine() : this(AudioEngineConfig.Default) { }

        public AudioEngine(AudioEngineConfig config)
        {
            _config = config;
            _volume = config.InitialVolume;
            _positionTimer = new Timer(OnPositionTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Load(IAudioReader reader, TrackInfo info)
        {
            Stop();
            lock (_audioLock)
            {
                _reader?.Dispose();
                _reader = reader;
                CurrentTrack = info;
                _waveProvider = new WaveChannel32(new WaveProviderToWaveStream(reader))
                {
                    Volume = _volume
                };
            }
        }

        public void Play()
        {
            if (_reader == null) return;

            lock (_audioLock)
            {
                if (_state == PlaybackState.Paused && _waveOut != null)
                {
                    _waveOut.Play();
                }
                else
                {
                    _waveOut?.Dispose();
                    _waveOut = CreateOutput();
                    _waveOut.PlaybackStopped += OnPlaybackStopped;
                    _waveOut.Init(_waveProvider);
                    _waveOut.Play();
                }
            }

            _state = PlaybackState.Playing;
            _positionTimer.Change(100, 100);
            StateChanged?.Invoke(this, _state);
        }

        public void Pause()
        {
            lock (_audioLock)
            {
                _waveOut?.Pause();
            }
            _state = PlaybackState.Paused;
            _positionTimer.Change(Timeout.Infinite, Timeout.Infinite);
            StateChanged?.Invoke(this, _state);
        }

        public void Stop()
        {
            lock (_audioLock)
            {
                _waveOut?.Stop();
                _waveOut?.Dispose();
                _waveOut = null;
            }
            _state = PlaybackState.Stopped;
            _positionTimer.Change(Timeout.Infinite, Timeout.Infinite);
            StateChanged?.Invoke(this, _state);
        }

        public void SwitchFormat(IAudioReader newReader, TrackInfo info)
        {
            if (newReader == null) return;
            if (info == null) return;

            var wasPlaying = _state == PlaybackState.Playing;
            Stop();

            _reader?.Dispose();
            _reader = newReader;
            CurrentTrack = info;
            _waveProvider = new WaveChannel32(new WaveProviderToWaveStream(newReader))
            {
                Volume = _volume
            };

            if (wasPlaying) Play();
        }

        private void AutoAdvance()
        {
            var next = Playlist.Next();
            if (next == null) return;
            try
            {
                var reader = FormatDetector.CreateReader(next.FilePath);
                Load(reader, next);
                Play();
            }
            catch { }
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                PlaybackError?.Invoke(this, e.Exception);
                return;
            }

            if (_reader != null && _reader.Position >= _reader.Duration - TimeSpan.FromMilliseconds(500))
            {
                _state = PlaybackState.Stopped;
                TrackEnded?.Invoke(this, EventArgs.Empty);
                AutoAdvance();
            }
        }

        private void OnPositionTick(object state)
        {
            if (_reader != null)
                PositionChanged?.Invoke(this, _reader.Position);
        }

        private IWavePlayer CreateOutput()
        {
            if (_config.PreferWasapi)
            {
                try
                {
                    return new WasapiOut(AudioClientShareMode.Shared, _config.BufferMilliseconds);
                }
                catch
                {
                }
            }
            return new DirectSoundOut(_config.BufferMilliseconds);
        }

        public void Dispose()
        {
            _positionTimer?.Dispose();
            Stop();
            _reader?.Dispose();
        }
    }
}
