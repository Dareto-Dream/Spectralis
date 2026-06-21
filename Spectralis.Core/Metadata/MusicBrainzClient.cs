using System.Text.Json;

namespace Spectralis.Core.Metadata;

public sealed record MusicBrainzRecording(
    string Id,
    string Title,
    string Artist,
    string Album,
    string ReleaseId,
    int Year,
    int TrackNumber)
{
    public string DisplayText =>
        $"{Artist} - {Title}" +
        (string.IsNullOrEmpty(Album) ? "" : $"  ·  {Album}") +
        (Year > 0 ? $" ({Year})" : "");
}

public static class MusicBrainzClient
{
    private static readonly HttpClient Http = new();

    static MusicBrainzClient()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Spectralis/1.0 (steak.nuggetincorperated@gmail.com)");
        Http.Timeout = TimeSpan.FromSeconds(10);
    }

    public static async Task<List<MusicBrainzRecording>> SearchAsync(
        string title, string artist, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title))
        {
            parts.Add($"recording:\"{title.Trim()}\"");
        }

        if (!string.IsNullOrWhiteSpace(artist))
        {
            parts.Add($"artist:\"{artist.Trim()}\"");
        }

        if (parts.Count == 0)
        {
            return [];
        }

        var query = Uri.EscapeDataString(string.Join(" AND ", parts));
        var url = $"https://musicbrainz.org/ws/2/recording/?query={query}&fmt=json&limit=10";

        using var resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("recordings", out var recordings))
        {
            return [];
        }

        var results = new List<MusicBrainzRecording>();
        foreach (var rec in recordings.EnumerateArray())
        {
            var id = rec.TryGetProperty("id", out var rid) ? rid.GetString() ?? "" : "";
            var recTitle = rec.TryGetProperty("title", out var rt) ? rt.GetString() ?? "" : "";

            var artistName = "";
            if (rec.TryGetProperty("artist-credit", out var credits) && credits.GetArrayLength() > 0)
            {
                var credit = credits[0];
                if (credit.TryGetProperty("artist", out var a) && a.TryGetProperty("name", out var n))
                {
                    artistName = n.GetString() ?? "";
                }
            }

            var album = "";
            var releaseId = "";
            var year = 0;
            var trackNum = 0;

            if (rec.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0)
            {
                var release = releases[0];
                album = release.TryGetProperty("title", out var rlt) ? rlt.GetString() ?? "" : "";
                releaseId = release.TryGetProperty("id", out var rlid) ? rlid.GetString() ?? "" : "";

                if (release.TryGetProperty("date", out var dateEl))
                {
                    var dateStr = dateEl.GetString() ?? "";
                    if (dateStr.Length >= 4 && int.TryParse(dateStr[..4], out var y))
                    {
                        year = y;
                    }
                }

                if (release.TryGetProperty("media", out var media) &&
                    media.GetArrayLength() > 0 &&
                    media[0].TryGetProperty("track", out var tracks) &&
                    tracks.GetArrayLength() > 0 &&
                    tracks[0].TryGetProperty("number", out var tn))
                {
                    int.TryParse(tn.GetString(), out trackNum);
                }
            }

            results.Add(new MusicBrainzRecording(id, recTitle, artistName, album, releaseId, year, trackNum));
        }

        return results;
    }

    public static async Task<byte[]?> FetchCoverArtAsync(string releaseId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(releaseId))
        {
            return null;
        }

        try
        {
            var url = $"https://coverartarchive.org/release/{releaseId}/front";
            using var resp = await Http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            return await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch
        {
            return null;
        }
    }
}
