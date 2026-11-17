using System.Net;
using System.Security.Cryptography;
using System.Text;
using Spectralis.Updater;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class RecoveryClientTests
{
    private const string Installer = "https://github.com/Dareto-Dream/Spectralis/releases/download/v7.1.2/Spectralis-win-x64-Setup.exe";

    [Theory]
    [InlineData("osx-x64")]
    [InlineData("osx-arm64")]
    public void MacRecoverySelectsItsStablePkgInstaller(string rid)
    {
        const string url = "https://github.com/Dareto-Dream/Spectralis/releases/download/v7.1.3/Spectralis-osx-x64-Setup.pkg";
        var json = """
            [{"version":"7.1.3","channel":"stable","platforms":{"osx-x64":{"installer":"$URL"}}}]
            """.Replace("$URL", url).Replace("osx-x64", rid);
        var expected = url.Replace("osx-x64", rid);
        Assert.Equal(expected, RecoveryClient.SelectLatest(json, rid).Installer.AbsoluteUri);
        Assert.True(RecoveryClient.IsTrustedInstaller(new Uri(expected), rid));
        Assert.False(RecoveryClient.IsTrustedInstaller(new Uri(expected), rid == "osx-x64" ? "osx-arm64" : "osx-x64"));
        Assert.False(RecoveryClient.IsTrustedInstaller(new Uri(expected.Replace(".pkg", ".zip")), rid));
        Assert.False(RecoveryClient.IsTrustedInstaller(new Uri(url), "win-x64"));
    }

    [Fact]
    public void RecoveryAlwaysChoosesTheNewestStableBuildWithAValidInstaller()
    {
        var json = """
        [
          {"version":"7.1.0","channel":"stable","platforms":{"win-x64":{"installer":"$OLDER"}}},
          {"version":"7.1.3-beta.1","channel":"beta","platforms":{"win-x64":{"installer":"$INSTALLER"}}},
          {"version":"7.1.2","channel":"stable","platforms":{"win-x64":{"installer":"$INSTALLER"}}},
          {"version":"9.0.0","channel":"stable","platforms":{"win-x64":{"installer":"https://example.com/evil.exe"}}}
        ]
        """.Replace("$OLDER", Installer.Replace("7.1.2", "7.1.0")).Replace("$INSTALLER", Installer);
        Assert.Equal("7.1.2", RecoveryClient.SelectLatest(json, "win-x64").Version);
        Assert.Throws<InvalidOperationException>(() => RecoveryClient.SelectLatest(json, "linux-x64"));
    }

    [Theory]
    [InlineData(Installer, true)]
    [InlineData("http://github.com/Dareto-Dream/Spectralis/releases/download/v7.1.2/Spectralis-win-x64-Setup.exe", false)]
    [InlineData("https://github.com.evil.com/Dareto-Dream/Spectralis/releases/download/v7.1.2/Spectralis-win-x64-Setup.exe", false)]
    [InlineData("https://github.com/SomeoneElse/Spectralis/releases/download/v7.1.2/Spectralis-win-x64-Setup.exe", false)]
    [InlineData(Installer + "?redirect=elsewhere", false)]
    public void DownloadsOnlyOurOwnReleaseInstallers(string url, bool expected) =>
        Assert.Equal(expected, RecoveryClient.IsTrustedInstaller(new Uri(url), "win-x64"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifiesTheEntireDownloadAndRemovesACorruptInstaller(bool corrupt)
    {
        var payload = Encoding.UTF8.GetBytes("test installer");
        using var http = new HttpClient(new Handler(payload));
        var client = new RecoveryClient(http);
        var path = Path.Combine(Path.GetTempPath(), "spectralis-installer-test-" + Guid.NewGuid().ToString("N"));
        var hash = Convert.ToHexString(SHA256.HashData(corrupt ? [1, 2, 3] : payload));
        try
        {
            var release = new RecoveryRelease("7.1.2", new Uri(Installer), hash, payload.Length);
            if (corrupt)
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, path, null, CancellationToken.None));
                Assert.False(File.Exists(path));
            }
            else
            {
                await client.DownloadAsync(release, path, null, CancellationToken.None);
                Assert.Equal(payload, File.ReadAllBytes(path));
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DoesNotOverwriteOrDeleteAnExistingFile()
    {
        using var http = new HttpClient(new Handler([1, 2, 3]));
        var path = Path.Combine(Path.GetTempPath(), "spectralis-installer-test-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "keep this");
        try
        {
            var release = new RecoveryRelease("7.1.2", new Uri(Installer), new string('A', 64), 3);
            await Assert.ThrowsAsync<IOException>(() => new RecoveryClient(http).DownloadAsync(release, path, null, CancellationToken.None));
            Assert.Equal("keep this", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    private sealed class Handler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }
}
