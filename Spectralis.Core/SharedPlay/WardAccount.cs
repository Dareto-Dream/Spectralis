using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Spectralis.Core.SharedPlay;

/// <summary>The app connects through Player's existing Ward OAuth client. No OAuth
/// secret ships in the desktop binary, and credentials never travel in URLs.</summary>
public static class WardAccount
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly string TokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectralis", "ward-account.dat");
    private static string? _token;
    private static string _origin = SharedPlayDefaults.CdnBaseUrl;
    public static string? Token => _token;
    public static string? DisplayName { get; private set; }
    public static bool IsConnected => !string.IsNullOrEmpty(_token);
    public static event Action? Changed;

    static WardAccount()
    {
        try
        {
            var bytes = File.ReadAllBytes(TokenPath);
            if (OperatingSystem.IsWindows()) bytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            using var saved = JsonDocument.Parse(bytes);
            _token = saved.RootElement.GetProperty("token").GetString();
            _origin = saved.RootElement.GetProperty("origin").GetString() ?? _origin;
            DisplayName = saved.RootElement.GetProperty("name").GetString();
        }
        catch { }
    }

    public static void Authorize(HttpRequestMessage request)
    {
        if (IsConnected && request.RequestUri?.GetLeftPart(UriPartial.Authority) == _origin)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    public static async Task<JsonElement> RequestAsync(Uri baseUri, string path, HttpMethod method, object? body = null, CancellationToken ct = default)
    {
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
        if (baseUri.Scheme != "https") throw new InvalidOperationException("Account connections require HTTPS.");
        var link = await RequestAsync(baseUri,"/player/v1/connect",HttpMethod.Post,ct:ct);
        var code = link.GetProperty("code").GetString();
        var secret = link.GetProperty("secret").GetString();
        var url = link.GetProperty("verificationUrl").GetString()!;
        if (!url.StartsWith("https://player.deltavdevs.com/connect/",StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected account connection URL.");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        while (true)
        {
            await Task.Delay(2500,deadline.Token);
            var result = await RequestAsync(baseUri,$"/player/v1/connect/{code}",HttpMethod.Post,new { secret },deadline.Token);
            if (result.GetProperty("status").GetString() != "connected") continue;
            _token = result.GetProperty("token").GetString();
            _origin = baseUri.GetLeftPart(UriPartial.Authority);
            var user = result.GetProperty("user");
            DisplayName = user.TryGetProperty("name",out var name) ? name.GetString() : "Ward account";
            Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { token = _token, origin = _origin, name = DisplayName });
            if (OperatingSystem.IsWindows()) bytes = ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);
            var options = new FileStreamOptions { Mode=FileMode.Create,Access=FileAccess.Write,Share=FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using (var file = new FileStream(TokenPath,options)) file.Write(bytes);
            Changed?.Invoke();
            return;
        }
    }

    public static async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) await RequestAsync(new Uri(_origin),"/player/v1/me",HttpMethod.Delete,ct:ct);
        _token = null;
        DisplayName = null;
        if (File.Exists(TokenPath)) File.Delete(TokenPath);
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
