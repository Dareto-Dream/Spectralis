using System;
using System.Collections.Generic;

namespace Spectralis.Core.Analysis
{
    public struct BpmResult
    {
        public float Bpm { get; init; }
        public float Confidence { get; init; }
        public bool IsValid => Bpm >= 40f && Bpm <= 220f && Confidence >= 0.4f;
    }

    public class BpmAnalyzer
    {
        private const int SampleRate = 44100;
        private const int WindowSize = 512;
        private const float MinBpm = 40f;
        private const float MaxBpm = 220f;

        public BpmResult Analyze(float[] samples, int sampleRate = SampleRate)
        {
            if (samples.Length < sampleRate) return default;

            var onsets = ComputeOnsets(samples);
            if (onsets.Count < 8) return default;

            return EstimateBpmFromOnsets(onsets, sampleRate);
        }

        private List<float> ComputeOnsets(float[] samples)
        {
            var onsets = new List<float>();
            float prevEnergy = 0f;
            int hopSize = WindowSize / 2;

            for (int i = 0; i + WindowSize < samples.Length; i += hopSize)
            {
                float energy = 0f;
                for (int j = i; j < i + WindowSize; j++)
                    energy += samples[j] * samples[j];
                energy /= WindowSize;

                if (energy > prevEnergy * 1.4f && energy > 0.001f)
                    onsets.Add(i / (float)SampleRate);

                prevEnergy = prevEnergy * 0.85f + energy * 0.15f;
            }
            return onsets;
        }

        private BpmResult EstimateBpmFromOnsets(List<float> onsets, int sampleRate)
        {
            var intervals = new List<float>();
            for (int i = 1; i < onsets.Count; i++)
                intervals.Add(onsets[i] - onsets[i - 1]);

            if (intervals.Count == 0) return default;

            intervals.Sort();
            float median = intervals[intervals.Count / 2];
            if (median < 0.001f) return default;

            float bpm = 60f / median;

            while (bpm < MinBpm) bpm *= 2;
            while (bpm > MaxBpm) bpm /= 2;

            float confidence = ComputeConfidence(intervals, 60f / bpm);

            return new BpmResult { Bpm = MathF.Round(bpm * 10f) / 10f, Confidence = confidence };
        }

        private float ComputeConfidence(List<float> intervals, float expectedInterval)
        {
            int matches = 0;
            float tolerance = expectedInterval * 0.08f;
            foreach (float iv in intervals)
            {
                float normalized = iv;
                while (normalized > expectedInterval * 1.5f) normalized /= 2f;
                if (MathF.Abs(normalized - expectedInterval) < tolerance) matches++;
            }
            return (float)matches / intervals.Count;
        }
    }
}
