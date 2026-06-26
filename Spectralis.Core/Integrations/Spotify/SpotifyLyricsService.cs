using System.Net.Http;
using System.Text;
using System.Text.Json;
using Spectralis.Core.Lyrics;

namespace Spectralis.Core.Integrations.Spotify;

public static class SpotifyLyricsService
{
    private const string ApiBase = "https://spotify-lyrics-api-production.up.railway.app/";
    private static readonly HttpClient Http = new();

    public static async Task<LyricsDocument?> FetchAsync(string trackId, CancellationToken ct = default)
    {
        var url = $"{ApiBase}?trackid={Uri.EscapeDataString(trackId)}&format=lrc";

        try
        {
            var json = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errEl) && errEl.GetBoolean())
                return null;

            if (!root.TryGetProperty("syncType", out var syncEl) || syncEl.GetString() != "LINE_SYNCED")
                return null;

            if (!root.TryGetProperty("lines", out var linesEl))
                return null;

            var sb = new StringBuilder();
            foreach (var line in linesEl.EnumerateArray())
            {
                var timeTag = line.TryGetProperty("timeTag", out var ttEl) ? ttEl.GetString() : null;
                var words = line.TryGetProperty("words", out var wEl) ? wEl.GetString() : null;
                if (string.IsNullOrEmpty(timeTag) || words is null)
                    continue;
                if (words == "♪")
                    continue;
                sb.Append('[').Append(timeTag).Append(']').AppendLine(words);
            }

            return LrcParser.Parse(sb.ToString(), "Spotify");
        }
        catch
        {
            return null;
        }
    }
}
