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
        private float _sensitivityMultiplier;
        private int _minFrameGap;

        public bool IsBeat { get; private set; }
        public float BeatStrength { get; private set; }
        public float CurrentEnergy { get; private set; }
        public float Sensitivity
        {
            get => _sensitivityMultiplier;
            set => _sensitivityMultiplier = Math.Clamp(value, 1.05f, 3f);
        }

        public int MinFrameGap
        {
            get => _minFrameGap;
            set => _minFrameGap = Math.Max(1, value);
        }

        public BeatDetector(int historySize = 43, float sensitivity = 1.3f, int minFrameGap = 8)
        {
            _historySize = historySize;
            _energyHistory = new Queue<float>(historySize);
            _sensitivityMultiplier = Math.Clamp(sensitivity, 1.05f, 3f);
            _minFrameGap = Math.Max(1, minFrameGap);
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

            bool beat = energy > avg * _sensitivityMultiplier && (_frameCount - _lastBeatFrame) > _minFrameGap;

            IsBeat = beat;
            BeatStrength = avg > 0 ? Math.Clamp(energy / (avg * _sensitivityMultiplier), 0f, 2f) : 0f;

            if (beat)
            {
                _lastBeatFrame = _frameCount;
                _lastBeatEnergy = energy;
            }

            _frameCount++;
        }
    }
}
