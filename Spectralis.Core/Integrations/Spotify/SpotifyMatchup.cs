using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spectralis.Core.Integrations.Spotify;

/// <summary>What Spotify says a track is. Enough to recognise the same recording somewhere else.</summary>
public sealed record SpotifyTrackFacts(
    string Id,
    string Title,
    string Artist,
    int DurationMs,
    string? ArtworkUrl = null,
    string? Album = null)
{
    public string Uri => $"spotify:track:{Id}";
}

/// <summary>A YouTube search result, as much as a flat yt-dlp search reports.</summary>
public sealed record YouTubeCandidate(string Id, string Title, string Channel, double DurationSeconds)
{
    public string Url => $"https://www.youtube.com/watch?v={Id}";
}

/// <summary>
/// Spotify matchup (beta): the rules for finding the YouTube upload of a Spotify track. No network
/// and no processes in here, so every rule is testable: reading Spotify's public embed page (which
/// needs neither the Web API nor an account), building search queries, and scoring candidates.
///
/// The bar is "the same recording", not "a video about the song". A wrong match plays the wrong
/// audio under the right title, which is worse than not matching, so anything doubtful is refused
/// and the caller falls back to the real Spotify player.
/// </summary>
public static partial class SpotifyMatchup
{
    /// <summary>Below this a candidate is not trusted. Tuned so a cover or a live cut never clears it.</summary>
    public const double AcceptScore = 0.62;

    public static string EmbedUrl(string kind, string id) => $"https://open.spotify.com/embed/{kind}/{id}";

    [GeneratedRegex("<script id=\"__NEXT_DATA__\"[^>]*>(?<json>.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex NextDataPattern();

    [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex BracketedPattern();

    [GeneratedRegex(@"\s+(-|–|—)\s+.*$")]
    private static partial Regex DashSuffixPattern();

    [GeneratedRegex(@"\b(feat|ft|featuring|with)\b\.?.*$", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturingPattern();

    /// <summary>
    /// Reads a track's facts out of <c>open.spotify.com/embed/track/{id}</c>. Returns null when the
    /// page has no usable track in it (removed, region-locked, or Spotify changed the markup).
    /// </summary>
    public static SpotifyTrackFacts? ParseTrackEmbed(string html)
    {
        using var doc = ParseEntity(html, out var entity);
        if (doc is null || Text(entity, "type") != "track")
        {
            return null;
        }

        var id = Text(entity, "id");
        var title = FirstNonEmpty(Text(entity, "name"), Text(entity, "title"));
        var artist = ArtistNames(entity);
        if (!IsTrackId(id) || title.Length == 0 || artist.Length == 0)
        {
            return null;
        }

        return new SpotifyTrackFacts(id, title, artist, Number(entity, "duration"), Artwork(entity));
    }

    /// <summary>
    /// Reads the track list out of an album or playlist embed page, in order. The embed page shows
    /// at most the first hundred or so tracks of a long playlist; that is all this returns.
    /// </summary>
    public static IReadOnlyList<SpotifyTrackFacts> ParseTrackListEmbed(string html)
    {
        using var doc = ParseEntity(html, out var entity);
        if (doc is null ||
            !entity.TryGetProperty("trackList", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var album = Text(entity, "type") == "album" ? FirstNonEmpty(Text(entity, "name"), Text(entity, "title")) : null;
        var artwork = Artwork(entity);
        var tracks = new List<SpotifyTrackFacts>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var uri = Text(item, "uri");
            var id = uri.StartsWith("spotify:track:", StringComparison.Ordinal) ? uri["spotify:track:".Length..] : string.Empty;
            var title = Text(item, "title");
            if (!IsTrackId(id) || title.Length == 0)
            {
                continue;
            }

            tracks.Add(new SpotifyTrackFacts(id, title, Text(item, "subtitle"), Number(item, "duration"), artwork, album));
        }

        return tracks;
    }

    public static bool IsTrackId(string? id) =>
        id is { Length: 22 } && id.All(char.IsAsciiLetterOrDigit);

    /// <summary>Pulls the id out of <c>spotify:track:{id}</c>; null for anything else.</summary>
    public static string? TrackIdFromUri(string? uri)
    {
        const string prefix = "spotify:track:";
        if (uri is null || !uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var id = uri[prefix.Length..];
        return IsTrackId(id) ? id : null;
    }

    /// <summary>
    /// Search phrases to try, most specific first. "Artist - Title" finds the official upload for
    /// most music; the "topic" variant reaches the auto-generated album track when the first
    /// search is drowned in lyric videos and reuploads.
    /// </summary>
    public static IReadOnlyList<string> BuildQueries(SpotifyTrackFacts track)
    {
        var artist = PrimaryArtist(track.Artist);
        var title = track.Title.Trim();
        var queries = new List<string> { $"{artist} - {title}" };
        var core = CoreTitle(title);
        if (!string.Equals(core, title, StringComparison.OrdinalIgnoreCase) && core.Length > 0)
        {
            queries.Add($"{artist} - {core}");
        }

        queries.Add($"{artist} {core} topic");
        return queries.Select(static q => q.Trim()).Where(static q => q.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The best candidate that clears <see cref="AcceptScore"/>, or null when none does.</summary>
    public static YouTubeCandidate? PickBest(SpotifyTrackFacts track, IEnumerable<YouTubeCandidate> candidates)
    {
        YouTubeCandidate? best = null;
        var bestScore = AcceptScore;
        foreach (var candidate in candidates)
        {
            var score = Score(track, candidate);
            if (score >= bestScore && (best is null || score > bestScore))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    // Words that mean "a different recording of this song". Each only counts against a candidate
    // when the Spotify title doesn't carry the same word: a track that really is the live version
    // should match the live upload.
    private static readonly string[] VariantWords =
    [
        "live", "cover", "karaoke", "instrumental", "remix", "acoustic", "nightcore", "sped", "slowed",
        "reverb", "8d", "reaction", "tutorial", "lesson", "mashup", "demo", "piano", "guitar", "drum",
        "bass boosted", "tribute", "parody", "concert", "session", "unplugged", "orchestral", "rerecorded",
    ];
    // Deliberately not here: "remaster", "version", "edit", "radio". A remaster is the same
    // performance, and a radio edit or extended cut is caught by its length instead.

    /// <summary>
    /// How sure we are that <paramref name="candidate"/> is the same recording, from 0 to 1.
    /// Length is the strongest signal (a different recording almost never matches to the second),
    /// then the title, then who uploaded it. Any hard mismatch returns 0 outright.
    /// </summary>
    public static double Score(SpotifyTrackFacts track, YouTubeCandidate candidate)
    {
        var wantedTitle = Tokens(CoreTitle(track.Title));
        var candidateText = Normalize($"{candidate.Title} {candidate.Channel}");
        var candidateTitleTokens = Tokens(candidate.Title);
        if (wantedTitle.Count == 0)
        {
            return 0;
        }

        // Every word of the song's title has to be there.
        var titleHits = wantedTitle.Count(candidateTitleTokens.Contains);
        if (titleHits < wantedTitle.Count)
        {
            return 0;
        }

        // A different-recording word the Spotify track doesn't have rules the candidate out.
        var spotifyText = Normalize(track.Title);
        foreach (var word in VariantWords)
        {
            if (ContainsWord(Normalize(candidate.Title), word) && !ContainsWord(spotifyText, word))
            {
                return 0;
            }
        }

        // Length. Unknown on either side is survivable only with everything else lining up.
        double durationScore;
        var wantedSeconds = track.DurationMs / 1000.0;
        if (wantedSeconds <= 0 || candidate.DurationSeconds <= 0)
        {
            durationScore = 0.35;
        }
        else
        {
            var off = Math.Abs(wantedSeconds - candidate.DurationSeconds);
            // A music video often carries a few seconds of intro or outro; an album cut does not.
            if (off > Math.Max(12, wantedSeconds * 0.06))
            {
                return 0;
            }

            durationScore = off <= 2 ? 1.0 : off <= 5 ? 0.85 : 0.6;
        }

        // The artist, in the title or as the channel.
        var artistTokens = Tokens(PrimaryArtist(track.Artist));
        var artistHits = artistTokens.Count == 0 ? 0 : artistTokens.Count(t => ContainsWord(candidateText, t));
        var artistScore = artistTokens.Count == 0 ? 0.5 : (double)artistHits / artistTokens.Count;
        if (artistScore < 0.5)
        {
            return 0;
        }

        // Who uploaded it. "<Artist> - Topic" is YouTube's own copy of the label's audio, the
        // closest thing to the Spotify file. An official channel or "official audio" is next.
        var channel = Normalize(candidate.Channel);
        var title = Normalize(candidate.Title);
        var sourceScore =
            channel.EndsWith(" topic", StringComparison.Ordinal) ? 1.0 :
            ContainsWord(title, "official audio") || ContainsWord(title, "audio") ? 0.85 :
            ContainsWord(channel, "vevo") || ContainsWord(title, "official") ? 0.75 :
            artistTokens.Count > 0 && artistTokens.All(t => ContainsWord(channel, t)) ? 0.75 :
            ContainsWord(title, "lyrics") || ContainsWord(title, "lyric") ? 0.45 :
            0.4;

        // Extra words in the candidate's title that the song doesn't have ("… but it's 10 hours").
        var known = new HashSet<string>(wantedTitle.Concat(artistTokens).Concat(Tokens(track.Artist)), StringComparer.Ordinal)
        {
            "official", "audio", "video", "music", "lyrics", "lyric", "hd", "hq", "4k", "visualizer", "visualiser", "mv", "feat", "ft", "the", "and", "a", "of",
        };
        var extra = candidateTitleTokens.Count(t => !known.Contains(t));
        var noisePenalty = Math.Min(0.25, Math.Max(0, extra - 2) * 0.05);

        return Math.Clamp(durationScore * 0.5 + artistScore * 0.2 + sourceScore * 0.3 - noisePenalty, 0, 1);
    }

    /// <summary>The song's own name: no "(feat. X)", "- 2011 Remaster" or "[Bonus Track]".</summary>
    public static string CoreTitle(string title)
    {
        var core = BracketedPattern().Replace(title, " ");
        core = DashSuffixPattern().Replace(core, " ");
        core = FeaturingPattern().Replace(core, " ");
        core = string.Join(' ', core.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        // A title that is nothing but a bracket ("(I Can't Get No) Satisfaction") keeps its words.
        return core.Length > 0 ? core : string.Join(' ', title.Replace('(', ' ').Replace(')', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>"Daft Punk, Pharrell Williams" -> "Daft Punk". Search and scoring go by the first credit.</summary>
    public static string PrimaryArtist(string artist)
    {
        // Not " & ": "Simon & Garfunkel" is one act, and a duo's upload names both anyway.
        var first = artist.Split([",", ";", " feat", " ft."], 2, StringSplitOptions.TrimEntries)[0];
        return first.Length > 0 ? first : artist.Trim();
    }

    private static HashSet<string> Tokens(string text) =>
        new(Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    private static bool ContainsWord(string normalizedHaystack, string word)
    {
        var needle = Normalize(word);
        return needle.Length > 0 && $" {normalizedHaystack} ".Contains($" {needle} ", StringComparison.Ordinal);
    }

    /// <summary>Lowercase, accents folded, punctuation to spaces: "Beyoncé – Déjà Vu!" -> "beyonce deja vu".</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (c is '\'' or '’')
            {
                // "don't" stays one word
            }
            else
            {
                sb.Append(' ');
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static JsonDocument? ParseEntity(string html, out JsonElement entity)
    {
        entity = default;
        var match = NextDataPattern().Match(html ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(match.Groups["json"].Value);
        }
        catch (JsonException)
        {
            return null;
        }

        var current = doc.RootElement;
        foreach (var name in new[] { "props", "pageProps", "state", "data", "entity" })
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
            {
                doc.Dispose();
                return null;
            }
        }

        if (current.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            return null;
        }

        entity = current;
        return doc;
    }

    private static string ArtistNames(JsonElement entity)
    {
        if (entity.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
        {
            var names = artists.EnumerateArray()
                .Select(static a => a.ValueKind == JsonValueKind.Object ? Text(a, "name") : string.Empty)
                .Where(static n => n.Length > 0)
                .ToList();
            if (names.Count > 0)
            {
                return string.Join(", ", names);
            }
        }

        return Text(entity, "subtitle");
    }

    /// <summary>The largest cover image listed, if it is an https link.</summary>
    private static string? Artwork(JsonElement entity)
    {
        if (!entity.TryGetProperty("visualIdentity", out var identity) ||
            identity.ValueKind != JsonValueKind.Object ||
            !identity.TryGetProperty("image", out var images) ||
            images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? best = null;
        var bestHeight = -1;
        foreach (var image in images.EnumerateArray())
        {
            if (image.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var url = Text(image, "url");
            var height = Number(image, "maxHeight");
            if (url.StartsWith("https://", StringComparison.Ordinal) && height > bestHeight)
            {
                best = url;
                bestHeight = height;
            }
        }

        return best;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n)
            ? (int)Math.Clamp(n, 0, int.MaxValue)
            : 0;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(static v => v.Length > 0) ?? string.Empty;
}
