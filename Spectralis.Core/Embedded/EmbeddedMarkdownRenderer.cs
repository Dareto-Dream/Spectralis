using Markdig;

namespace Spectralis.Core.Embedded;

public static class EmbeddedMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static EmbeddedHtmlContext ToHtmlContext(EmbeddedMarkdownContext markdown)
    {
        var html = ToHtmlPage(markdown);
        var htmlBytes = System.Text.Encoding.UTF8.GetBytes(html);
        return new EmbeddedHtmlContext(
            markdown.Id,
            htmlBytes,
            new Dictionary<string, byte[]>(),
            null,
            markdown.Version);
    }

    public static string ToHtmlPage(EmbeddedMarkdownContext context)
    {
        var markdownText = System.Text.Encoding.UTF8.GetString(context.MarkdownBytes);
        var body = Markdown.ToHtml(markdownText, Pipeline);
        var title = System.Net.WebUtility.HtmlEncode(context.DisplayName);
        var customCss = string.IsNullOrWhiteSpace(context.CssOverride)
            ? ""
            : $"<style>{context.CssOverride}</style>";

        // $$""" raw string: {{ }} are literal braces; {{expr}} is interpolation.
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>{{title}}</title>
{{customCss}}
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body {
    background: #0a0a0a;
    color: #e8e8e8;
    font-family: system-ui, -apple-system, sans-serif;
    font-size: 15px;
    line-height: 1.65;
    padding: 32px 40px 48px;
    max-width: 780px;
    margin: 0 auto;
  }
  h1, h2, h3, h4 { color: #ffffff; margin: 1.4em 0 0.5em; line-height: 1.25; }
  h1 { font-size: 2em; border-bottom: 1px solid #2a2a2a; padding-bottom: 0.3em; }
  h2 { font-size: 1.4em; border-bottom: 1px solid #1e1e1e; padding-bottom: 0.2em; }
  p { margin: 0.8em 0; }
  a { color: #7db8f7; text-decoration: none; }
  a:hover { text-decoration: underline; }
  code { background: #1a1a1a; border-radius: 4px; padding: 0.15em 0.4em; font-size: 0.88em; }
  pre { background: #111; border-radius: 6px; padding: 14px 16px; overflow-x: auto; margin: 1em 0; }
  pre code { background: none; padding: 0; }
  blockquote { border-left: 3px solid #3a3a3a; padding-left: 16px; color: #aaa; margin: 1em 0; }
  img { max-width: 100%; border-radius: 4px; }
  hr { border: none; border-top: 1px solid #2a2a2a; margin: 1.5em 0; }
  ul, ol { padding-left: 1.6em; margin: 0.8em 0; }
  li { margin: 0.3em 0; }
  table { border-collapse: collapse; width: 100%; margin: 1em 0; }
  th, td { border: 1px solid #2a2a2a; padding: 8px 12px; text-align: left; }
  th { background: #141414; color: #fff; }
</style>
</head>
<body>
{{body}}
</body>
</html>
""";
    }
}
