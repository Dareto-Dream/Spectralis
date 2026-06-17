using System.Text.Json;

namespace Spectralis.Core.SharedPlay
{
    public static class SharedPlayProtocol
    {
        public const string VersionHeader = "X-Spectralis-Protocol";
        public const string Version = "1.0";

        public static string Serialize<T>(T message) =>
            JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        public static T? Deserialize<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        public static string Ping() => """{"type":"ping"}""";
        public static string Pong() => """{"type":"pong"}""";

        public static bool IsPing(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "ping";
            }
            catch { return false; }
        }
    }
}
