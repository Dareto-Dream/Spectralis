namespace Spectralis.Streaming
{
    public class SpotifyAuthConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = "http://localhost:5009/callback";
        public string[] Scopes { get; set; } = new[]
        {
            "user-read-playback-state",
            "user-modify-playback-state",
            "user-read-currently-playing",
            "streaming",
            "user-read-email",
            "user-read-private"
        };
    }

    public class SpotifyTokenSet
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
        public System.DateTime IssuedAt { get; set; }

        public bool IsExpired => (System.DateTime.UtcNow - IssuedAt).TotalSeconds >= ExpiresIn - 60;
    }
}
