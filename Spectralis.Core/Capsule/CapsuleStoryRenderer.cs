using System.Text;
using System.Text.Json;
using Spectralis.Core.Embedded;

namespace Spectralis.Core.Capsule;

/// <summary>
/// Synthesizes a self-contained HTML visual-novel pager from a capsule's story pages[].
/// Used when a .spectralis capsule declares story.mode = "visual-novel" and has pages[].
/// </summary>
public static class CapsuleStoryRenderer
{
    public static EmbeddedHtmlContext? TryToHtmlContext(CapsuleStory story, Func<string, byte[]?> tryReadEntry)
    {
        // pages[] preferred; chapters[] is the older alias
        var rawPages = story.Pages.Count > 0 ? story.Pages
                     : story.Chapters.Count > 0 ? story.Chapters
                     : null;

        List<StoryPage>? pages = null;
        if (rawPages is not null)
        {
            pages = BuildPages(rawPages, tryReadEntry);
        }

        // Synthesize a single-page story from backstory when no structured pages exist
        if ((pages is null || pages.Count == 0) && !string.IsNullOrWhiteSpace(story.Backstory))
        {
            var coverUri = TryReadStoryImage(story, tryReadEntry);
            pages = [new StoryPage(story.Backstory.Trim(), coverUri, null)];
        }

        if (pages is null || pages.Count == 0) return null;

        var html = BuildHtmlPage(story, pages);
        return new EmbeddedHtmlContext(
            "capsule-story",
            Encoding.UTF8.GetBytes(html),
            new Dictionary<string, byte[]>(),
            null,
            null);
    }

    private sealed record StoryPage(string Text, string? ImageDataUri, string? BackgroundDataUri);

    private static List<StoryPage> BuildPages(IEnumerable<JsonElement> rawPages, Func<string, byte[]?> tryReadEntry)
    {
        var result = new List<StoryPage>();
        foreach (var el in rawPages)
        {
            var text = TryGetString(el, "text") ?? TryGetString(el, "content") ?? "";
            var imageUri = TryReadImageEntry(el, "image", tryReadEntry)
                        ?? TryReadImageEntry(el, "characterImage", tryReadEntry)
                        ?? TryReadImageEntry(el, "explainerImage", tryReadEntry);
            var bgUri    = TryReadImageEntry(el, "background", tryReadEntry)
                        ?? TryReadImageEntry(el, "backgroundImage", tryReadEntry);
            result.Add(new StoryPage(text, imageUri, bgUri));
        }
        return result;
    }

    private static string? TryReadStoryImage(CapsuleStory story, Func<string, byte[]?> tryReadEntry)
    {
        foreach (var entry in new[] { story.ImageEntry, story.ExplainerImage, story.CharacterImage, story.Image })
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var bytes = tryReadEntry(entry);
            if (bytes is null || bytes.Length == 0) continue;
            return $"data:{GuessMime(entry)};base64,{Convert.ToBase64String(bytes)}";
        }
        return null;
    }

    private static string? TryGetString(JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }

    private static string? TryReadImageEntry(JsonElement el, string property, Func<string, byte[]?> tryReadEntry)
    {
        var entry = TryGetString(el, property);
        if (string.IsNullOrWhiteSpace(entry)) return null;
        var bytes = tryReadEntry(entry);
        if (bytes is null || bytes.Length == 0) return null;
        var mime = GuessMime(entry);
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string GuessMime(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp"           => "image/webp",
            ".gif"            => "image/gif",
            ".svg"            => "image/svg+xml",
            _                 => "image/png",
        };
    }

    private static string BuildHtmlPage(CapsuleStory story, List<StoryPage> pages)
    {
        var pagesJson = BuildPagesJson(pages);
        var title = System.Net.WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(story.Summary) ? "Capsule Story" : story.Summary);

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>{{title}}</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body { width: 100%; height: 100%; overflow: hidden; }
  body {
    background: #0a0a0a;
    color: #e8e8e8;
    font-family: system-ui, -apple-system, sans-serif;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 100%;
  }
  #stage {
    position: relative;
    width: 100%;
    max-width: 820px;
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }
  #bg {
    position: absolute;
    inset: 0;
    background-size: cover;
    background-position: center;
    opacity: 0.18;
    transition: background-image 0.4s;
    pointer-events: none;
  }
  #content {
    position: relative;
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: flex-end;
    padding: 24px 32px 0;
    gap: 16px;
  }
  #char-img {
    max-height: 280px;
    object-fit: contain;
    opacity: 0;
    transition: opacity 0.3s;
    border-radius: 4px;
  }
  #char-img.visible { opacity: 1; }
  #text-box {
    width: 100%;
    background: rgba(0,0,0,0.75);
    border: 1px solid rgba(255,255,255,0.08);
    border-radius: 8px;
    padding: 20px 24px;
    min-height: 80px;
    font-size: 15px;
    line-height: 1.7;
    white-space: pre-wrap;
    backdrop-filter: blur(8px);
    -webkit-backdrop-filter: blur(8px);
  }
  #nav {
    display: flex;
    align-items: center;
    gap: 16px;
    padding: 14px 0 16px;
  }
  button {
    background: rgba(255,255,255,0.08);
    border: 1px solid rgba(255,255,255,0.15);
    border-radius: 6px;
    color: #e8e8e8;
    font-size: 13px;
    padding: 8px 20px;
    cursor: pointer;
    transition: background 0.15s;
  }
  button:hover { background: rgba(255,255,255,0.15); }
  button:disabled { opacity: 0.3; cursor: default; }
  #counter {
    font-size: 12px;
    color: #666;
    min-width: 64px;
    text-align: center;
  }
</style>
</head>
<body>
<div id="stage">
  <div id="bg"></div>
  <div id="content">
    <img id="char-img" alt="">
    <div id="text-box"></div>
  </div>
</div>
<div id="nav">
  <button id="btn-prev" disabled>&#8592; Back</button>
  <span id="counter">1 / 1</span>
  <button id="btn-next">Next &#8594;</button>
</div>
<script>
(function() {
  var pages = {{pagesJson}};
  var idx = 0;
  var bg = document.getElementById('bg');
  var charImg = document.getElementById('char-img');
  var textBox = document.getElementById('text-box');
  var counter = document.getElementById('counter');
  var btnPrev = document.getElementById('btn-prev');
  var btnNext = document.getElementById('btn-next');

  function show(i) {
    var p = pages[i];
    textBox.textContent = p.text;
    if (p.bg) {
      bg.style.backgroundImage = 'url(' + JSON.stringify(p.bg) + ')';
    } else {
      bg.style.backgroundImage = 'none';
    }
    if (p.img) {
      charImg.src = p.img;
      charImg.className = 'visible';
    } else {
      charImg.src = '';
      charImg.className = '';
    }
    counter.textContent = (i + 1) + ' / ' + pages.length;
    btnPrev.disabled = (i === 0);
    btnNext.disabled = (i === pages.length - 1);
    btnNext.textContent = (i === pages.length - 1) ? 'Done ✓' : 'Next →';
  }

  btnPrev.addEventListener('click', function() { if (idx > 0) show(--idx); });
  btnNext.addEventListener('click', function() { if (idx < pages.length - 1) show(++idx); });

  document.addEventListener('keydown', function(e) {
    if (e.key === 'ArrowRight' || e.key === 'Enter' || e.key === ' ') {
      if (idx < pages.length - 1) { show(++idx); e.preventDefault(); }
    } else if (e.key === 'ArrowLeft') {
      if (idx > 0) { show(--idx); e.preventDefault(); }
    }
  });

  show(0);
})();
</script>
</body>
</html>
""";
    }

    private static string BuildPagesJson(List<StoryPage> pages)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < pages.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var p = pages[i];
            sb.Append('{');
            sb.Append($"\"text\":{JsonSerializer.Serialize(p.Text)}");
            sb.Append($",\"img\":{(p.ImageDataUri is null ? "null" : JsonSerializer.Serialize(p.ImageDataUri))}");
            sb.Append($",\"bg\":{(p.BackgroundDataUri is null ? "null" : JsonSerializer.Serialize(p.BackgroundDataUri))}");
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }
}
