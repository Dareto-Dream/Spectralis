using Xunit;
using Spectralis.Core.Integrations;
using Spectralis.Core.Integrations.Spotify;

namespace Spectralis.Tests.Core;

public class SpotifyMatchupTests
{
    private const string TrackId = "4cOdK2wGLETKBW3PvgPWqT";

    private static readonly SpotifyTrackFacts Rick =
        new(TrackId, "Never Gonna Give You Up", "Rick Astley", 213_573);

    private static string Embed(string entityJson) =>
        "<html><body><script id=\"__NEXT_DATA__\" type=\"application/json\">" +
        "{\"props\":{\"pageProps\":{\"state\":{\"data\":{\"entity\":" + entityJson + "}}}}}" +
        "</script></body></html>";

    [Fact]
    public void ATrackEmbedPageGivesTheTrackFacts()
    {
        var html = Embed($$"""
        { "type": "track", "name": "Never Gonna Give You Up", "id": "{{TrackId}}", "title": "Never Gonna Give You Up",
          "artists": [ { "name": "Rick Astley" }, { "name": "Someone Else" } ], "duration": 213573,
          "visualIdentity": { "image": [
            { "url": "https://image-cdn.example/small", "maxHeight": 64 },
            { "url": "https://image-cdn.example/large", "maxHeight": 640 },
            { "url": "http://image-cdn.example/insecure", "maxHeight": 9000 } ] } }
        """);

        var facts = SpotifyMatchup.ParseTrackEmbed(html)!;
        Assert.Equal(TrackId, facts.Id);
        Assert.Equal("Never Gonna Give You Up", facts.Title);
        Assert.Equal("Rick Astley, Someone Else", facts.Artist);
        Assert.Equal(213_573, facts.DurationMs);
        Assert.Equal("https://image-cdn.example/large", facts.ArtworkUrl);
        Assert.Equal($"spotify:track:{TrackId}", facts.Uri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>no data here</html>")]
    [InlineData("<script id=\"__NEXT_DATA__\">{ not json</script>")]
    [InlineData("<script id=\"__NEXT_DATA__\">{\"props\":{}}</script>")]
    public void APageWithoutATrackIsNullNotACrash(string html)
    {
        Assert.Null(SpotifyMatchup.ParseTrackEmbed(html));
        Assert.Empty(SpotifyMatchup.ParseTrackListEmbed(html));
    }

    [Fact]
    public void AnEmbedForSomethingElseOrWithABadIdIsRefused()
    {
        Assert.Null(SpotifyMatchup.ParseTrackEmbed(Embed("""{ "type": "album", "name": "x", "id": "4cOdK2wGLETKBW3PvgPWqT" }""")));
        Assert.Null(SpotifyMatchup.ParseTrackEmbed(Embed("""{ "type": "track", "name": "x", "id": "../../etc", "artists": [ { "name": "a" } ] }""")));
    }

    [Fact]
    public void AnAlbumEmbedPageGivesItsTracksInOrder()
    {
        var html = Embed("""
        { "type": "album", "name": "Whenever You Need Somebody", "subtitle": "Rick Astley",
          "trackList": [
            { "uri": "spotify:track:7GhIk7Il098yCjg4BQjzvb", "title": "Never Gonna Give You Up", "subtitle": "Rick Astley", "duration": 212826 },
            { "uri": "spotify:episode:7GhIk7Il098yCjg4BQjzvb", "title": "Not a track", "subtitle": "x", "duration": 1 },
            { "uri": "spotify:track:0000000000000000000000", "title": "", "subtitle": "x", "duration": 1 },
            { "uri": "spotify:track:1111111111111111111111", "title": "Together Forever", "subtitle": "Rick Astley", "duration": 205000 }
          ] }
        """);

        var tracks = SpotifyMatchup.ParseTrackListEmbed(html);
        Assert.Equal(["Never Gonna Give You Up", "Together Forever"], tracks.Select(t => t.Title));
        Assert.All(tracks, t => Assert.Equal("Whenever You Need Somebody", t.Album));
        Assert.Equal("7GhIk7Il098yCjg4BQjzvb", tracks[0].Id);
    }

    [Theory]
    [InlineData("spotify:track:4cOdK2wGLETKBW3PvgPWqT", "4cOdK2wGLETKBW3PvgPWqT")]
    [InlineData("spotify:album:4cOdK2wGLETKBW3PvgPWqT", null)]
    [InlineData("spotify:track:short", null)]
    [InlineData("spotify:track:4cOdK2wGLETKBW3PvgPW/../", null)]
    [InlineData(null, null)]
    public void TrackIdsComeOnlyFromTrackUris(string? uri, string? expected) =>
        Assert.Equal(expected, SpotifyMatchup.TrackIdFromUri(uri));

    [Fact]
    public void TitlesAndArtistsAreReducedToWhatIdentifiesTheSong()
    {
        Assert.Equal("Bohemian Rhapsody", SpotifyMatchup.CoreTitle("Bohemian Rhapsody - Remastered 2011"));
        Assert.Equal("Get Lucky", SpotifyMatchup.CoreTitle("Get Lucky (feat. Pharrell Williams & Nile Rodgers)"));
        Assert.Equal("Satisfaction", SpotifyMatchup.CoreTitle("(I Can't Get No) Satisfaction"));
        Assert.Equal("Intro", SpotifyMatchup.CoreTitle("(Intro)")); // nothing but a bracket keeps its words
        Assert.Equal("Daft Punk", SpotifyMatchup.PrimaryArtist("Daft Punk, Pharrell Williams"));
        Assert.Equal("Simon & Garfunkel", SpotifyMatchup.PrimaryArtist("Simon & Garfunkel"));
        Assert.Equal("beyonce deja vu", SpotifyMatchup.Normalize("Beyoncé – Déjà Vu!"));
        Assert.Equal("dont stop", SpotifyMatchup.Normalize("Don't Stop"));
    }

    [Fact]
    public void QueriesStartSpecificAndIncludeTheTopicUpload()
    {
        var queries = SpotifyMatchup.BuildQueries(
            new SpotifyTrackFacts(TrackId, "Get Lucky (feat. Pharrell Williams)", "Daft Punk, Pharrell Williams", 248_000));
        Assert.Equal("Daft Punk - Get Lucky (feat. Pharrell Williams)", queries[0]);
        Assert.Contains("Daft Punk - Get Lucky", queries);
        Assert.Contains("Daft Punk Get Lucky topic", queries);
    }

    [Fact]
    public void TheLabelsOwnAudioBeatsEverythingElse()
    {
        var topic = new YouTubeCandidate("aaaaaaaaaaa", "Never Gonna Give You Up", "Rick Astley - Topic", 213);
        var video = new YouTubeCandidate("bbbbbbbbbbb", "Rick Astley - Never Gonna Give You Up (Official Video)", "Rick Astley", 212);
        var lyrics = new YouTubeCandidate("ccccccccccc", "Rick Astley - Never Gonna Give You Up (Lyrics)", "LyricLand", 214);

        Assert.True(SpotifyMatchup.Score(Rick, topic) > SpotifyMatchup.Score(Rick, video));
        Assert.True(SpotifyMatchup.Score(Rick, video) > SpotifyMatchup.Score(Rick, lyrics));
        Assert.Same(topic, SpotifyMatchup.PickBest(Rick, [lyrics, video, topic]));
    }

    [Theory]
    [InlineData("Rick Astley - Never Gonna Give You Up (Live at Wembley)", "Rick Astley", 213)]   // another performance
    [InlineData("Never Gonna Give You Up - Rick Astley (Cover)", "Some Band", 213)]              // a cover
    [InlineData("Rick Astley - Never Gonna Give You Up (Karaoke)", "KaraokeHits", 213)]
    [InlineData("Rick Astley - Never Gonna Give You Up (Sped Up)", "tiktok sounds", 180)]
    [InlineData("Rick Astley - Never Gonna Give You Up 10 HOURS", "Loops", 36000)]               // wrong length
    [InlineData("Never Gonna Give You Up", "Barry Gibb - Topic", 213)]                           // wrong artist
    [InlineData("Rick Astley - Together Forever", "Rick Astley", 213)]                           // wrong song
    [InlineData("Rick Astley - Never Gonna Give You Up (Piano Tutorial)", "PianoGuy", 213)]
    public void DifferentRecordingsAreRefused(string title, string channel, double seconds)
    {
        var candidate = new YouTubeCandidate("aaaaaaaaaaa", title, channel, seconds);
        Assert.Equal(0, SpotifyMatchup.Score(Rick, candidate));
        Assert.Null(SpotifyMatchup.PickBest(Rick, [candidate]));
    }

    [Fact]
    public void ALiveTrackMatchesItsLiveUpload()
    {
        var live = new SpotifyTrackFacts(TrackId, "Hotel California - Live On MTV, 1994", "Eagles", 432_000);
        var upload = new YouTubeCandidate("aaaaaaaaaaa", "Hotel California (Live On MTV, 1994)", "Eagles - Topic", 432);
        Assert.NotNull(SpotifyMatchup.PickBest(live, [upload]));
    }

    [Fact]
    public void ARemasterOfTheSameRecordingStillMatches()
    {
        var track = new SpotifyTrackFacts(TrackId, "Bohemian Rhapsody - Remastered 2011", "Queen", 354_000);
        var upload = new YouTubeCandidate("aaaaaaaaaaa", "Queen – Bohemian Rhapsody (Official Video Remastered)", "Queen Official", 359);
        Assert.NotNull(SpotifyMatchup.PickBest(track, [upload]));
    }

    [Fact]
    public void WithoutALengthOnlyTheLabelsOwnAudioIsTrusted()
    {
        var noLength = Rick with { DurationMs = 0 };
        var topic = new YouTubeCandidate("aaaaaaaaaaa", "Never Gonna Give You Up", "Rick Astley - Topic", 213);
        var reupload = new YouTubeCandidate("bbbbbbbbbbb", "Rick Astley Never Gonna Give You Up", "randomuser42", 213);
        Assert.Same(topic, SpotifyMatchup.PickBest(noLength, [reupload, topic]));
        Assert.Null(SpotifyMatchup.PickBest(noLength, [reupload]));
    }

    [Fact]
    public void SearchOutputIsParsedAndJunkLinesAreSkipped()
    {
        var stdout = string.Join('\n',
            "WARNING: something",
            """{"id":"dQw4w9WgXcQ","title":"Rick Astley - Never Gonna Give You Up","channel":"Rick Astley","duration":212.0}""",
            """{"id":"UCuAXFkgsw1L7xaCfnd5JJOw","title":"A channel row","channel":"x","duration":null}""",
            """{"id":"abcdefghijk","title":"No channel field","uploader":"Uploader Name"}""",
            """{"id":"truncated""");

        var results = YtDlpService.ParseCandidateSearch(stdout);
        Assert.Equal(2, results.Count);
        Assert.Equal(("dQw4w9WgXcQ", "Rick Astley - Never Gonna Give You Up", "Rick Astley", 212.0), results[0]);
        Assert.Equal(("abcdefghijk", "No channel field", "Uploader Name", 0.0), results[1]);
    }

    [Fact]
    public void TheSearchQueryCanNotBecomeAnOption()
    {
        var args = YtDlpService.BuildCandidateSearchArguments("--exec rm -rf /\n--another", 50);
        var sentinel = Array.IndexOf(args, "--");
        Assert.True(sentinel >= 0, "the query must come after a -- sentinel");
        Assert.Equal(args.Length - 2, sentinel);
        Assert.Equal("ytsearch10:--exec rm -rf / --another", args[^1]);
    }
}
