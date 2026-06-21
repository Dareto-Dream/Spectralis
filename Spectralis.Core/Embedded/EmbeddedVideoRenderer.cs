using System.Text;

namespace Spectralis.Core.Embedded;

public static class EmbeddedVideoRenderer
{
    public static EmbeddedHtmlContext ToHtmlContext(EmbeddedVideoContext video)
    {
        var htmlBytes = Encoding.UTF8.GetBytes(ToHtmlPage(video));
        return new EmbeddedHtmlContext(
            video.Id,
            htmlBytes,
            new Dictionary<string, byte[]>(),
            null,
            video.Version);
    }

    public static string ToHtmlPage(EmbeddedVideoContext video)
    {
        var mime = video.Codec.ToLowerInvariant() switch
        {
            "mp4" or "h264" or "avc" or "h.264" => "video/mp4",
            "webm" or "vp8" or "vp9" or "av1"  => "video/webm",
            "ogg" or "theora"                   => "video/ogg",
            _                                   => "video/mp4",
        };

        var base64 = Convert.ToBase64String(video.VideoBytes);
        var autoplay = video.Autoplay ? "autoplay" : string.Empty;
        var loop    = video.Loop    ? "loop"     : string.Empty;

        var widthStyle  = video.Width  is { } w ? $"width:{w}px;" : "width:100%;";
        var heightStyle = video.Height is { } h ? $"height:{h}px;" : "height:100%;";

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { width: 100%; height: 100%; background: #000; overflow: hidden; display: flex; align-items: center; justify-content: center; }
  video { {{widthStyle}}{{heightStyle}}object-fit: contain; display: block; }
</style>
</head>
<body>
<video {{autoplay}} {{loop}} preload="auto">
  <source src="data:{{mime}};base64,{{base64}}" type="{{mime}}">
</video>
<script>
  document.querySelector('video').addEventListener('ended', function() {
    if ({{(video.Loop ? "false" : "true")}}) this.currentTime = 0;
  });
</script>
</body>
</html>
""";
    }
}
