using System;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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

        public event EventHandler<PlaybackState> StateChanged;
        public event EventHandler<TimeSpan> PositionChanged;
        public event EventHandler TrackEnded;
        public event EventHandler<Exception> PlaybackError;

        public PlaybackState State => _state;
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
                if (_reader != null)
                    _reader.Position = value;
            }
        }

        public TimeSpan Duration => _reader?.Duration ?? TimeSpan.Zero;

        public TrackInfo CurrentTrack { get; private set; }

        public AudioEngine()
        {
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
                    _waveOut = new WasapiOut();
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
            }
        }

        private void OnPositionTick(object state)
        {
            if (_reader != null)
                PositionChanged?.Invoke(this, _reader.Position);
        }

        public void Dispose()
        {
            _positionTimer?.Dispose();
            Stop();
            _reader?.Dispose();
        }
    }
}
