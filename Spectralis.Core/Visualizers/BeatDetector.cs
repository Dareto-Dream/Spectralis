using System;
using System.Collections.Generic;
using Spectralis.Core.Audio;

namespace Spectralis.Core.Visualizers
{
    public class BeatDetector
    {
        private readonly Queue<float> _energyHistory;
        private readonly int _historySize;
        private float _lastBeatEnergy;
        private long _lastBeatFrame;
        private long _frameCount;
        private const float SensitivityMultiplier = 1.3f;

        public bool IsBeat { get; private set; }
        public float BeatStrength { get; private set; }
        public float CurrentEnergy { get; private set; }

        public BeatDetector(int historySize = 43)
        {
            _historySize = historySize;
            _energyHistory = new Queue<float>(historySize);
        }

        public void Process(in AudioFrame frame)
        {
            float energy = 0f;
            int bassEnd = Math.Min(8, frame.Spectrum.Length);
            for (int i = 0; i < bassEnd; i++)
                energy += frame.Spectrum[i] * frame.Spectrum[i];
            energy /= bassEnd;

            CurrentEnergy = energy;

            if (_energyHistory.Count >= _historySize) _energyHistory.Dequeue();
            _energyHistory.Enqueue(energy);

            float avg = 0f;
            foreach (float e in _energyHistory) avg += e;
            avg /= _energyHistory.Count;

            bool beat = energy > avg * SensitivityMultiplier && (_frameCount - _lastBeatFrame) > 8;

            IsBeat = beat;
            BeatStrength = avg > 0 ? Math.Clamp(energy / (avg * SensitivityMultiplier), 0f, 2f) : 0f;

            if (beat)
            {
                _lastBeatFrame = _frameCount;
                _lastBeatEnergy = energy;
            }

            _frameCount++;
        }
    }
}
