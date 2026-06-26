using System.Reflection;

namespace Spectralis.Core.Integrations.Spotify;

public static class SpotifyClientIdProvider
{
    public const string EnvironmentVariable = "SPECTRALIS_SPOTIFY_CLIENT_ID";

    private const string MetadataKey = "SpotifyClientId";
    private static readonly Lazy<string> DefaultClientIdValue = new(LoadDefaultClientId);

    public static string DefaultClientId => DefaultClientIdValue.Value;
    public static bool HasDefaultClientId => DefaultClientId.Length > 0;

    public static string ResolveClientId(string? userClientId)
    {
        var cleanedUserClientId = Clean(userClientId);
        return cleanedUserClientId.Length > 0 ? cleanedUserClientId : DefaultClientId;
    }

    private static string LoadDefaultClientId()
    {
        var entryAssemblyClientId = ReadAssemblyMetadata(Assembly.GetEntryAssembly());
        if (entryAssemblyClientId.Length > 0)
            return entryAssemblyClientId;

        var libraryAssemblyClientId = ReadAssemblyMetadata(typeof(SpotifyClientIdProvider).Assembly);
        if (libraryAssemblyClientId.Length > 0)
            return libraryAssemblyClientId;

        return Clean(Environment.GetEnvironmentVariable(EnvironmentVariable));
    }

    private static string ReadAssemblyMetadata(Assembly? assembly)
    {
        if (assembly is null)
            return "";

        foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (attribute.Key == MetadataKey)
                return Clean(attribute.Value);
        }

        return "";
    }

    private static string Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
}
