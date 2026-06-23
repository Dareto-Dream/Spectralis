using Spectralis.Core.Metadata;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class TrackMetadataReaderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { }
        }
    }

    private string Track(string name, byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"spectralis-meta-{Guid.NewGuid():N}{name}");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void ValidWav_ReadsAudioProperties()
    {
        var path = WavFixture.CreateSineWav(seconds: 0.3, sampleRate: 44100, channels: 2);
        _tempFiles.Add(path);

        var info = TrackMetadataReader.Read(path);

        Assert.Equal(44100, info.SampleRateHz);
        Assert.Equal(2, info.Channels);
        Assert.True(info.FileSizeBytes > 0);
        Assert.True(info.Duration > TimeSpan.Zero);
        // No tags in a generated WAV: display title falls back to the file name.
        Assert.Equal(Path.GetFileNameWithoutExtension(path), info.DisplayTitle);
    }

    [Fact]
    public void GarbageBytes_DoesNotThrow()
    {
        var path = Track(".mp3", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02 });

        var info = TrackMetadataReader.Read(path);

        Assert.Equal(path, info.SourcePath);
        Assert.Equal(7, info.FileSizeBytes);
        Assert.Equal(Path.GetFileNameWithoutExtension(path), info.DisplayTitle);
    }

    [Fact]
    public void ZeroByteFile_DoesNotThrow()
    {
        var path = Track(".flac", Array.Empty<byte>());

        var info = TrackMetadataReader.Read(path);

        Assert.Equal(0, info.FileSizeBytes);
        Assert.Equal(TimeSpan.Zero, info.Duration);
    }

    [Fact]
    public void MissingFile_DoesNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".mp3");

        var info = TrackMetadataReader.Read(path);

        Assert.Equal(path, info.SourcePath);
        Assert.Equal(0, info.FileSizeBytes);
    }
}
