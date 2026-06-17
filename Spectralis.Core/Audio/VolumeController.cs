using System;

namespace Spectralis.Core.Audio
{
    public class VolumeController
    {
        private float _volume = 1.0f;
        private float _premuteVolume = 1.0f;
        private bool _muted;

        public event EventHandler<float>? VolumeChanged;

        public float Volume
        {
            get => _muted ? 0f : _volume;
            set
            {
                _volume = Math.Clamp(value, 0f, 1f);
                VolumeChanged?.Invoke(this, Volume);
            }
        }

        public bool Muted
        {
            get => _muted;
            set
            {
                if (_muted == value) return;
                _muted = value;
                VolumeChanged?.Invoke(this, Volume);
            }
        }

        public void ToggleMute()
        {
            if (_muted)
            {
                _muted = false;
                _volume = _premuteVolume;
            }
            else
            {
                _premuteVolume = _volume;
                _muted = true;
            }
            VolumeChanged?.Invoke(this, Volume);
        }

        public void Nudge(float delta)
        {
            Volume = _volume + delta;
        }
    }
}
