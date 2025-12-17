using System;

namespace Spectralis.Core.Analysis
{
    public enum MusicalKey
    {
        C, CSharp, D, DSharp, E, F, FSharp, G, GSharp, A, ASharp, B,
        Cm, CSharpM, Dm, DSharpM, Em, Fm, FSharpM, Gm, GSharpM, Am, ASharpM, Bm
    }

    public struct KeyResult
    {
        public MusicalKey Key { get; init; }
        public float Confidence { get; init; }
        public string Name { get; init; }
        public bool IsMajor => (int)Key < 12;
        public bool IsValid => Confidence >= 0.35f;
    }

    public class KeyAnalyzer
    {
        private static readonly float[] MajorProfile = { 6.35f, 2.23f, 3.48f, 2.33f, 4.38f, 4.09f, 2.52f, 5.19f, 2.39f, 3.66f, 2.29f, 2.88f };
        private static readonly float[] MinorProfile = { 6.33f, 2.68f, 3.52f, 5.38f, 2.60f, 3.53f, 2.54f, 4.75f, 3.98f, 2.69f, 3.34f, 3.17f };

        private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        public KeyResult Analyze(float[] chromagram)
        {
            if (chromagram.Length < 12) return default;

            float[] chroma = NormalizeChroma(chromagram);

            float bestScore = float.MinValue;
            int bestKey = 0;
            bool bestMajor = true;

            for (int root = 0; root < 12; root++)
            {
                float majorScore = Correlate(chroma, MajorProfile, root);
                float minorScore = Correlate(chroma, MinorProfile, root);

                if (majorScore > bestScore) { bestScore = majorScore; bestKey = root; bestMajor = true; }
                if (minorScore > bestScore) { bestScore = minorScore; bestKey = root; bestMajor = false; }
            }

            float confidence = NormalizeScore(bestScore);
            var key = bestMajor ? (MusicalKey)bestKey : (MusicalKey)(bestKey + 12);
            string name = NoteNames[bestKey] + (bestMajor ? " Major" : " Minor");

            return new KeyResult { Key = key, Confidence = confidence, Name = name };
        }

        private float[] NormalizeChroma(float[] chroma)
        {
            float[] norm = new float[12];
            float sum = 0f;
            for (int i = 0; i < 12; i++) sum += chroma[i % 12];
            if (sum < 0.001f) return norm;
            for (int i = 0; i < 12; i++) norm[i] = chroma[i % 12] / sum;
            return norm;
        }

        private float Correlate(float[] chroma, float[] profile, int root)
        {
            float sum = 0f;
            float chromaMean = 0f, profileMean = 0f;
            for (int i = 0; i < 12; i++) { chromaMean += chroma[i]; profileMean += profile[i]; }
            chromaMean /= 12; profileMean /= 12;

            float num = 0f, denomA = 0f, denomB = 0f;
            for (int i = 0; i < 12; i++)
            {
                float c = chroma[(i + root) % 12] - chromaMean;
                float p = profile[i] - profileMean;
                num += c * p;
                denomA += c * c;
                denomB += p * p;
            }
            float denom = MathF.Sqrt(denomA * denomB);
            return denom < 0.001f ? 0f : num / denom;
        }

        private float NormalizeScore(float score) => (score + 1f) / 2f;
    }
}
