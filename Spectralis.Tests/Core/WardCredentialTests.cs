using System.Security.Cryptography;
using System.Text.Json;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class WardCredentialTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "spectralis-ward-test-" + Guid.NewGuid().ToString("N"));
    private string TokenPath => Path.Combine(directory, "account.dat");
    private static readonly Uri Production = new(SharedPlayDefaults.CdnBaseUrl);
    private static readonly Uri Staging = new(SharedPlayDefaults.StagingCdnBaseUrl);

    [Fact]
    public void LegacyCredentialKeepsItsOriginalOriginAndSurvivesSavingAnotherAccount()
    {
        Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { token = "production-test", origin = Production.ToString(), name = "Production" });
        if (OperatingSystem.IsWindows()) bytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenPath, bytes);
        var store = new WardCredentialStore(TokenPath);
        Assert.Null(store.Get(Staging));
        Assert.Equal("production-test", store.Get(Production)?.Token);
        store.Set(Staging, "staging-test", "Staging");
        var reloaded = new WardCredentialStore(TokenPath);
        Assert.Equal("production-test", reloaded.Get(Production)?.Token);
        Assert.Equal("staging-test", reloaded.Get(Staging)?.Token);
        reloaded.Remove(Staging);
        reloaded = new WardCredentialStore(TokenPath);
        Assert.Null(reloaded.Get(Staging));
        Assert.Equal("production-test", reloaded.Get(Production)?.Token);
    }

    [Theory]
    [InlineData("https://spectralis-api.deltavdevs.com/path", true)]
    [InlineData("https://spectralis-api.deltavdevs.com:443/path", true)]
    [InlineData("https://spectralis-api.deltavdevs.com:444/path", false)]
    [InlineData("http://spectralis-api.deltavdevs.com/path", false)]
    [InlineData("https://spectralis-api.deltavdevs.com.evil.test/path", false)]
    [InlineData(SharedPlayDefaults.StagingCdnBaseUrl, false)]
    public void HttpCredentialsRequireTheExactHttpsOrigin(string url, bool authorized)
    {
        var store = new WardCredentialStore(TokenPath);
        store.Set(Production, "test-token", "Test");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        store.Authorize(request);
        Assert.Equal(authorized ? "test-token" : null, request.Headers.Authorization?.Parameter);
        Assert.Equal(authorized, SharedPlayDefaults.SupportsWard(new Uri(url)));
    }

    [Fact]
    public void SocketCredentialsRequireSecureMatchingOrigin()
    {
        var store = new WardCredentialStore(TokenPath);
        store.Set(Production, "test-token", "Test");
        Assert.Equal("test-token", store.GetSocketToken(new Uri("wss://spectralis-api.deltavdevs.com/socket")));
        Assert.Null(store.GetSocketToken(new Uri("ws://spectralis-api.deltavdevs.com/socket")));
        Assert.Null(store.GetSocketToken(new Uri("wss://spectralis-api.deltavdevs.com:444/socket")));
        Assert.Null(store.GetSocketToken(new Uri("wss://audioplayer-staging.up.railway.app/socket")));
    }

    [Fact]
    public async Task DevelopmentCannotStartWardOrUseItsSavedCredentials()
    {
        Assert.False(WardAccount.IsConnectedTo(Staging));
        Assert.Null(WardAccount.GetDisplayName(Staging));
        Assert.Null(WardAccount.GetSocketToken(new Uri("wss://audioplayer-staging.up.railway.app/socket")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => WardAccount.ConnectAsync(Staging, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => WardAccount.DisconnectAsync(Staging));
        await Assert.ThrowsAsync<InvalidOperationException>(() => WardAccount.RequestAsync(Staging, "/player/v1/me", HttpMethod.Get));
    }

    [Fact]
    public void DevelopmentIgnoresSavedPermanentRoomSettings()
    {
        using var controller = new SharedPlaySessionController();
        controller.ApplySettings(true, Production.ToString(), true, "production-room", "test-owner", "Room");
        Assert.NotNull(controller.Snapshot.ChannelUrl);
        controller.ApplySettings(true, Staging.ToString(), true, "production-room", "test-owner", "Room");
        Assert.True(controller.Snapshot.IsEnabled);
        Assert.Null(controller.Snapshot.ChannelUrl);
        Assert.Equal(Staging.Host, controller.GetCdnBaseUriPublic().Host);
    }

    [Theory]
    [InlineData("https://player.deltavdevs.com/connect/test", true)]
    [InlineData("https://player.deltavdevs.com.evil.test/connect/test", false)]
    [InlineData("http://player.deltavdevs.com/connect/test", false)]
    [InlineData("https://player.deltavdevs.com/connect/other", false)]
    [InlineData("https://player.deltavdevs.com/connect/test?redirect=elsewhere", false)]
    public void ConnectionApprovalMustMatchProductionPlayerAndCode(string url, bool valid)
    {
        if (valid) WardAccount.ValidateVerificationUrl(Production, url, "test");
        else Assert.Throws<InvalidOperationException>(() => WardAccount.ValidateVerificationUrl(Production, url, "test"));
    }

    public void Dispose()
    {
        if (File.Exists(TokenPath)) File.Delete(TokenPath);
        if (Directory.Exists(directory)) Directory.Delete(directory);
    }
}
