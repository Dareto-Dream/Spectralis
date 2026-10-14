using System.Runtime.InteropServices;
using Spectralis.Core.Analysis;

namespace Spectralis.Core.Audio.Dj;

public sealed record DjStemInput(string Path, string Name, int AudioStreamIndex = 0);
public sealed record DjContainerStream(int AudioIndex, string Name, string Description, bool IsMaster = false);

public sealed class DjTrack : IDisposable
{
    public const int SampleRate = 48000;
    public IReadOnlyList<DjPcmAsset> Stems { get; }
    public string Path { get; }
    public double Duration => Stems.Max(s => s.Frames) / (double)SampleRate;
    public float[] Waveform { get; }
    public double Bpm { get; private set; }
    public double BeatOffset { get; private set; }
    public string Key { get; private set; } = "";

    private DjTrack(IReadOnlyList<DjPcmAsset> stems, string path)
    {
        Stems = stems;
        Path = path;
        Waveform = new float[2048];
        foreach (var stem in stems)
            for (var i = 0; i < Waveform.Length; i++)
            {
                // Place shorter stems against the common timeline, rather than stretching them.
                var target = (int)(i * stem.Frames / (Duration * SampleRate));
                if (target < Waveform.Length) Waveform[target] = Math.Min(1, Waveform[target] + stem.Waveform[i]);
            }
    }

    public static async Task<DjTrack> LoadAsync(IReadOnlyList<DjStemInput> inputs, CancellationToken ct = default, string? identityPath = null)
    {
        if (inputs.Count is < 1 or > 8) throw new ArgumentException("Choose between one and eight audio files or stems.");
        var assets = new List<DjPcmAsset>();
        try
        {
            foreach (var input in inputs)
            {
                if (!File.Exists(input.Path)) throw new FileNotFoundException("Audio file not found.", input.Path);
                assets.Add(await DjPcmAsset.LoadAsync(input, ct).ConfigureAwait(false));
            }
            var track = new DjTrack(assets, identityPath ?? inputs[0].Path);
            var (bpm, offset) = await BpmAnalyzer.AnalyzeAsync(assets[0].DecodedPath, ct).ConfigureAwait(false);
            track.Bpm = bpm;
            track.BeatOffset = offset.TotalSeconds;
            track.Key = await Task.Run(() => KeyAnalyzer.AnalyzeFile(assets[0].DecodedPath, ct), ct).ConfigureAwait(false);
            return track;
        }
        catch { foreach (var asset in assets) asset.Dispose(); throw; }
    }

    public static Task<IReadOnlyList<DjContainerStream>> ProbeAsync(string path, CancellationToken ct = default) =>
        DjTrackLoader.ProbeAsync(path, ct);

    public void Dispose() { foreach (var stem in Stems) stem.Dispose(); }
}

/// <summary>Seekable stereo PCM with a small read cache; accessed only under the mixer lock.</summary>
public sealed class DjPcmAsset : IDisposable
{
    private readonly FfmpegWaveStream _stream;
    private readonly byte[] _cache = new byte[8192 * 8];
    private long _cacheStart = -1;
    private int _cacheFrames;
    public string Name { get; }
    public long Frames => _stream.Length / 8;
    internal string DecodedPath => _stream.DecodedPath;
    public float[] Waveform { get; } = new float[2048];

    private DjPcmAsset(FfmpegWaveStream stream, string name) { _stream = stream; Name = name; }

    public static async Task<DjPcmAsset> LoadAsync(DjStemInput input, CancellationToken ct)
    {
        var stream = await FfmpegWaveStream.OpenAsync(input.Path, audioStreamIndex: input.AudioStreamIndex,
            cancellationToken: ct, sampleRate: DjTrack.SampleRate, channels: 2).ConfigureAwait(false);
        var asset = new DjPcmAsset(stream, input.Name);
        try
        {
            await Task.Run(() =>
            {
                var bytes = new byte[65536];
                long frame = 0;
                int read;
                while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var samples = MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, read));
                    for (var i = 0; i + 1 < samples.Length; i += 2, frame++)
                    {
                        var bin = (int)Math.Min(2047, frame * 2048 / Math.Max(1, asset.Frames));
                        asset.Waveform[bin] = Math.Max(asset.Waveform[bin], Math.Max(Math.Abs(samples[i]), Math.Abs(samples[i + 1])));
                    }
                }
                stream.Position = 0;
            }, ct).ConfigureAwait(false);
            return asset;
        }
        catch { asset.Dispose(); throw; }
    }

    public float Sample(long frame, int channel)
    {
        if (frame < 0 || frame >= Frames) return 0;
        if (frame < _cacheStart || frame >= _cacheStart + _cacheFrames)
        {
            _cacheStart = frame;
            _stream.Position = frame * 8;
            _cacheFrames = _stream.Read(_cache, 0, _cache.Length) / 8;
        }
        var index = (int)(frame - _cacheStart);
        return index < _cacheFrames ? MemoryMarshal.Cast<byte, float>(_cache)[index * 2 + channel] : 0;
    }

    public void Dispose() => _stream.Dispose();
}
