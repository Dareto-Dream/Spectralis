using Spectralis.Core.Metadata;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class TagEditorTests : IDisposable
{
    private readonly string _path;

    public TagEditorTests()
    {
        // A real WAV fixture so TagLib can tag it (RIFF INFO).
        _path = WavFixture.CreateSineWav(0.2);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
            File.Delete(_path + ".bak");
        }
        catch
        {
        }
    }

    [Fact]
    public void WriteThenRead_RoundTripsCoreFields()
    {
        var model = TagEditorService.Read(_path);
        model.Title = "Edited Title";
        model.Artist = "Edited Artist";
        model.Album = "Edited Album";
        model.Year = 2024;
        model.TrackNumber = 7;
        model.Genre = "Test Genre";

        TagEditorService.Write(model);
        var reread = TagEditorService.Read(_path);

        Assert.Equal("Edited Title", reread.Title);
        Assert.Equal("Edited Artist", reread.Artist);
        Assert.Equal("Edited Album", reread.Album);
        Assert.Equal(2024u, reread.Year);
        Assert.Equal(7u, reread.TrackNumber);
        Assert.Equal("Test Genre", reread.Genre);
    }

    [Fact]
    public void Write_CreatesBackupOnce()
    {
        var original = File.ReadAllBytes(_path);

        var model = TagEditorService.Read(_path);
        model.Title = "First";
        TagEditorService.Write(model);

        Assert.True(File.Exists(_path + ".bak"));
        Assert.Equal(original, File.ReadAllBytes(_path + ".bak"));

        // A second save must not overwrite the original backup.
        model.Title = "Second";
        TagEditorService.Write(model);
        Assert.Equal(original, File.ReadAllBytes(_path + ".bak"));
    }
}
