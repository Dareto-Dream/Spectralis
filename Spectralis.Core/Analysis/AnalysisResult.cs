namespace Spectralis.Core.Analysis
{
    public class AnalysisResult
    {
        public string FilePath { get; init; } = string.Empty;
        public BpmResult Bpm { get; init; }
        public KeyResult Key { get; init; }
        public BeatGrid BeatGrid { get; init; } = new();
        public float LoudnessLufs { get; init; }
        public float DynamicRange { get; init; }
    }
}
