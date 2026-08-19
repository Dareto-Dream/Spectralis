using System.Text;
using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class CapsuleStoryRendererTests
{
    [Fact]
    public void TryToHtmlContext_CustomEntry_UsesCreatorHtmlOverPager()
    {
        var story = new CapsuleStory
        {
            Entry = "story/index.html",
            BinaryAssets = new Dictionary<string, string> { ["bg"] = "story/bg.webp" },
            DataAssets = new Dictionary<string, string> { ["config"] = "story/config.json" },
            Backstory = "This pager text should never be used.",
        };

        var entries = new Dictionary<string, byte[]>
        {
            ["story/index.html"] = Encoding.UTF8.GetBytes("<html><body>hi</body></html>"),
            ["story/bg.webp"] = [1, 2, 3],
            ["story/config.json"] = Encoding.UTF8.GetBytes("{\"a\":1}"),
        };

        var context = CapsuleStoryRenderer.TryToHtmlContext(story, e => entries.GetValueOrDefault(e));

        Assert.NotNull(context);
        Assert.Equal("<html><body>hi</body></html>", Encoding.UTF8.GetString(context!.HtmlBytes));
        Assert.Equal(new byte[] { 1, 2, 3 }, context.BinaryAssets["bg"]);
        Assert.Equal("{\"a\":1}", context.TextAssets["config"]);
    }

    [Fact]
    public void TryToHtmlContext_CustomEntryMissingFile_FallsBackToPager()
    {
        var story = new CapsuleStory
        {
            Entry = "story/index.html",
            Backstory = "Fallback pager text.",
        };

        var context = CapsuleStoryRenderer.TryToHtmlContext(story, _ => null);

        Assert.NotNull(context);
        Assert.Contains("Fallback pager text.", Encoding.UTF8.GetString(context!.HtmlBytes));
    }

    [Fact]
    public void TryToHtmlContext_NoEntry_SynthesizesPagerFromBackstory()
    {
        var story = new CapsuleStory { Backstory = "Just a backstory blurb." };

        var context = CapsuleStoryRenderer.TryToHtmlContext(story, _ => null);

        Assert.NotNull(context);
        Assert.Contains("Just a backstory blurb.", Encoding.UTF8.GetString(context!.HtmlBytes));
    }

    [Fact]
    public void TryToHtmlContext_NoStoryContentAtAll_ReturnsNull()
    {
        var story = new CapsuleStory();

        var context = CapsuleStoryRenderer.TryToHtmlContext(story, _ => null);

        Assert.Null(context);
    }
}
