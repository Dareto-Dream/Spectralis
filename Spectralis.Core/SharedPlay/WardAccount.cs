using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Spectralis.Core.SharedPlay;

/// <summary>The app connects through Player's existing Ward OAuth client. No OAuth
/// secret ships in the desktop binary, and credentials never travel in URLs.</summary>
public static class WardAccount
{
    private static readonly HttpClient Http = Spectralis.Core.Diagnostics.NetworkClients.Create("ward", TimeSpan.FromSeconds(20));
    private static readonly string TokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectralis", "ward-account.dat");
    private static readonly WardCredentialStore Accounts = new(TokenPath);
    public static bool IsConnectedTo(Uri backend) => SharedPlayDefaults.SupportsWard(backend) && Accounts.Get(backend) is not null;
    public static string? GetDisplayName(Uri backend) => IsConnectedTo(backend) ? Accounts.Get(backend)?.Name : null;
    public static string? GetSocketToken(Uri socketUri) => socketUri.IsAbsoluteUri && socketUri.Scheme == "wss" &&
        SharedPlayDefaults.SupportsWard(new UriBuilder(socketUri) { Scheme = "https", Port = socketUri.IsDefaultPort ? -1 : socketUri.Port }.Uri)
        ? Accounts.GetSocketToken(socketUri) : null;
    public static event Action? Changed;

    public static void Authorize(HttpRequestMessage request)
    {
        if (request.RequestUri is { } uri && SharedPlayDefaults.SupportsWard(uri)) Accounts.Authorize(request);
    }

    public static Uri GetRoomSettingsUrl(Uri backend, string roomId)
    {
        RequireWard(backend);
        return new Uri("https://player.deltavdevs.com/rooms/" + Uri.EscapeDataString(roomId) + "/settings");
    }

    private static void RequireWard(Uri backend)
    {
        if (!SharedPlayDefaults.SupportsWard(backend))
            throw new InvalidOperationException("Development uses temporary rooms only. Ward sign-in is disabled.");
    }

    public static async Task<JsonElement> RequestAsync(Uri baseUri, string path, HttpMethod method, object? body = null, CancellationToken ct = default)
    {
        RequireWard(baseUri);
        using var request = new HttpRequestMessage(method, new Uri(baseUri, path)) { Content = body is null ? null : JsonContent.Create(body) };
        Authorize(request);
        using var response = await Http.SendAsync(request, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(doc.RootElement.TryGetProperty("error",out var error) ? error.GetString() : "The player service could not complete that request.");
        return doc.RootElement.Clone();
    }

    public static async Task ConnectAsync(Uri baseUri, CancellationToken ct)
    {
        RequireWard(baseUri);
        if (baseUri.Scheme != "https") throw new InvalidOperationException("Account connections require HTTPS.");
        var link = await RequestAsync(baseUri,"/player/v1/connect",HttpMethod.Post,ct:ct);
        var code = link.GetProperty("code").GetString();
        var secret = link.GetProperty("secret").GetString();
        var url = link.GetProperty("verificationUrl").GetString()!;
        ValidateVerificationUrl(baseUri, url, code);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        while (true)
        {
            await Task.Delay(2500,deadline.Token);
            var result = await RequestAsync(baseUri,$"/player/v1/connect/{code}",HttpMethod.Post,new { secret },deadline.Token);
            if (result.GetProperty("status").GetString() != "connected") continue;
            var user = result.GetProperty("user");
            Accounts.Set(baseUri, result.GetProperty("token").GetString()!, user.TryGetProperty("name",out var name) ? name.GetString() : "Ward account");
            Changed?.Invoke();
            return;
        }
    }

    internal static void ValidateVerificationUrl(Uri backend, string url, string? code)
    {
        RequireWard(backend);
        if (string.IsNullOrWhiteSpace(code) || code.Any(c => !char.IsAsciiLetterOrDigit(c)) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var target) || WardCredentialStore.Origin(target) is null ||
            target.AbsolutePath != $"/connect/{code}" || target.Query.Length != 0 || target.Fragment.Length != 0)
            throw new InvalidOperationException("Unexpected account connection URL.");
        if (target.GetLeftPart(UriPartial.Authority) != "https://player.deltavdevs.com")
            throw new InvalidOperationException("Unexpected account connection URL.");
    }

    public static async Task DisconnectAsync(Uri backend, CancellationToken ct = default)
    {
        RequireWard(backend);
        if (IsConnectedTo(backend)) await RequestAsync(backend,"/player/v1/me",HttpMethod.Delete,ct:ct);
        Accounts.Remove(backend);
        Changed?.Invoke();
    }
}

internal sealed class WardHttpHandler : DelegatingHandler
{
    public WardHttpHandler() : base(new HttpClientHandler()) { }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        WardAccount.Authorize(request);
        return base.SendAsync(request,ct);
    }
}
