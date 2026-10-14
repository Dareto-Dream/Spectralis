using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Spectralis.Core.SharedPlay;

internal sealed record WardCredential(string Token, string? Name);

/// <summary>Device tokens belong to the backend that issued them, including its port.</summary>
internal sealed class WardCredentialStore
{
    private readonly string path;
    private readonly object gate = new();
    private Dictionary<string, WardCredential> accounts = new(StringComparer.Ordinal);

    internal WardCredentialStore(string path)
    {
        this.path = path;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (OperatingSystem.IsWindows()) bytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            using var saved = JsonDocument.Parse(bytes);
            // Keep the original origin when migrating the old single-account file.
            if (saved.RootElement.TryGetProperty("accounts", out var entries))
            {
                foreach (var entry in entries.EnumerateArray()) ReadEntry(entry);
            }
            else ReadEntry(saved.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException or KeyNotFoundException) { }
    }

    internal static string? Origin(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
        ? uri.GetLeftPart(UriPartial.Authority) : null;

    private void ReadEntry(JsonElement entry)
    {
        if (!entry.TryGetProperty("origin", out var origin) || !Uri.TryCreate(origin.GetString(), UriKind.Absolute, out var uri) || Origin(uri) is not { } key) return;
        if (!entry.TryGetProperty("token", out var token) || string.IsNullOrWhiteSpace(token.GetString())) return;
        accounts[key] = new(token.GetString()!, entry.TryGetProperty("name", out var name) ? name.GetString() : null);
    }

    internal WardCredential? Get(Uri uri)
    {
        var key = Origin(uri);
        lock (gate) return key is not null && accounts.TryGetValue(key, out var credential) ? credential : null;
    }

    internal string? GetSocketToken(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "wss") return null;
        return Get(new UriBuilder(uri) { Scheme = "https", Port = uri.IsDefaultPort ? -1 : uri.Port }.Uri)?.Token;
    }

    internal void Authorize(HttpRequestMessage request)
    {
        if (request.RequestUri is { } uri && Get(uri) is { } credential)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
    }

    internal void Set(Uri uri, string token, string? name)
    {
        var key = Origin(uri) ?? throw new InvalidOperationException("Account connections require an HTTPS origin.");
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Ward did not issue a device token.");
        lock (gate)
        {
            var updated = new Dictionary<string, WardCredential>(accounts, StringComparer.Ordinal) { [key] = new(token, name) };
            Save(updated);
            accounts = updated;
        }
    }

    internal void Remove(Uri uri)
    {
        if (Origin(uri) is not { } key) return;
        lock (gate)
        {
            var updated = new Dictionary<string, WardCredential>(accounts, StringComparer.Ordinal);
            if (!updated.Remove(key)) return;
            Save(updated);
            accounts = updated;
        }
    }

    private void Save(Dictionary<string, WardCredential> updated)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = 2, accounts = updated.Select(a => new { origin = a.Key, token = a.Value.Token, name = a.Value.Name }) });
        if (OperatingSystem.IsWindows()) bytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporaryPath, options)) { file.Write(bytes); file.Flush(true); }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
