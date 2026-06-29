using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class CapsuleReaderTests : IDisposable
{
    private readonly string _dir;
    private readonly CapsuleFixture _fixture = new();

    public CapsuleReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"spectralis-capsule-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Read_ValidCapsule_VerifiesAndParsesManifest()
    {
        var path = _fixture.WriteCapsule(_dir);

        var package = CapsuleReader.Read(path);

        Assert.Equal("Test Title", package.Manifest.Title);
        Assert.Equal(_fixture.Fingerprint, package.Fingerprint);
        Assert.NotNull(package.TryReadEntry("audio/track.wav"));
        Assert.Contains("manifest.json", package.EntryNames());
    }

    [Fact]
    public void Read_TamperedPayload_ThrowsSignatureInvalid()
    {
        var path = _fixture.WriteCapsule(_dir);
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF; // flip a payload byte after signing
        File.WriteAllBytes(path, bytes);

        var ex = Assert.Throws<InvalidDataException>(() => CapsuleReader.Read(path));
        Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_TamperedSignature_Throws()
    {
        var path = _fixture.WriteCapsule(_dir);
        var bytes = File.ReadAllBytes(path);
        bytes[50] ^= 0xFF; // inside the 64-byte signature block
        File.WriteAllBytes(path, bytes);

        Assert.Throws<InvalidDataException>(() => CapsuleReader.Read(path));
    }

    [Fact]
    public void Read_BadMagic_Throws()
    {
        var path = _fixture.WriteCapsule(_dir);
        var bytes = File.ReadAllBytes(path);
        bytes[0] = 0x00;
        File.WriteAllBytes(path, bytes);

        var ex = Assert.Throws<InvalidDataException>(() => CapsuleReader.Read(path));
        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_TooSmallFile_Throws()
    {
        var path = Path.Combine(_dir, "tiny.spectralis");
        File.WriteAllBytes(path, new byte[10]);

        Assert.Throws<InvalidDataException>(() => CapsuleReader.Read(path));
    }

    [Fact]
    public void Read_ManifestFingerprintMismatch_Throws()
    {
        var path = Path.Combine(_dir, "mismatch.spectralis");
        File.WriteAllBytes(path, _fixture.BuildCapsuleBytes(
            manifestFingerprintOverride: new string('0', 64)));

        var ex = Assert.Throws<InvalidDataException>(() => CapsuleReader.Read(path));
        Assert.Contains("fingerprint", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryReadEntry_MissingEntryReturnsNull()
    {
        var package = CapsuleReader.Read(_fixture.WriteCapsule(_dir));
        Assert.Null(package.TryReadEntry("does/not/exist.bin"));
    }
}

public sealed class CapsuleTrustRuntimeTests : IDisposable
{
    private readonly string _dir;
    private readonly CapsuleFixture _fixture = new();
    private readonly StubCdnHandler _cdn = new();
    private readonly CreatorTrustStore _trustStore;
    private readonly CapsuleTrustRuntime _runtime;

    public CapsuleTrustRuntimeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"spectralis-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _trustStore = new CreatorTrustStore(Path.Combine(_dir, "trusted-creators.json"));
        _runtime = new CapsuleTrustRuntime(
            _trustStore,
            new CapsuleCdnClient(new HttpClient(_cdn), new Uri("https://cdn.test/")));
    }

    public void Dispose()
    {
        _runtime.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static Func<CapsuleTrustContext, Task<bool>> Prompt(bool answer, Action? onPrompt = null) =>
        _ =>
        {
            onPrompt?.Invoke();
            return Task.FromResult(answer);
        };

    [Fact]
    public async Task Open_UnknownCreator_PromptsOnceThenTrusts()
    {
        _cdn.SetKey(_fixture.MakeKeyMetadata(active: true));
        var path = _fixture.WriteCapsule(_dir);
        var prompts = 0;

        var first = await _runtime.OpenAsync(path, Prompt(true, () => prompts++), CancellationToken.None);
        var second = await _runtime.OpenAsync(path, Prompt(true, () => prompts++), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, prompts);
        Assert.True(_trustStore.IsTrusted(_fixture.Fingerprint));
    }

    [Fact]
    public async Task Open_UserDeniesTrust_Fails()
    {
        _cdn.SetKey(_fixture.MakeKeyMetadata(active: true));
        var path = _fixture.WriteCapsule(_dir);

        var result = await _runtime.OpenAsync(path, Prompt(false), CancellationToken.None);

        Assert.Equal(CapsuleOpenStatus.UserDenied, result.Status);
        Assert.False(_trustStore.IsTrusted(_fixture.Fingerprint));
    }

    [Fact]
    public async Task Open_RevokedKey_AlwaysRejected()
    {
        _cdn.SetKey(_fixture.MakeKeyMetadata(active: false));
        var path = _fixture.WriteCapsule(_dir);

        var result = await _runtime.OpenAsync(path, Prompt(true), CancellationToken.None);

        Assert.Equal(CapsuleOpenStatus.KeyRevoked, result.Status);
    }

    [Fact]
    public async Task Open_RevokedKeyInCache_RejectsEvenIfTrusted()
    {
        // Trusted earlier, then the key gets revoked: the cached revocation plus
        // CDN refresh must reject without silently allowing.
        _trustStore.Trust(_fixture.Fingerprint, "Test Creator");
        _trustStore.CacheMetadata(_fixture.Fingerprint, _fixture.MakeKeyMetadata(active: false));
        _cdn.SetKey(_fixture.MakeKeyMetadata(active: false));
        var path = _fixture.WriteCapsule(_dir);

        var result = await _runtime.OpenAsync(path, Prompt(true), CancellationToken.None);

        Assert.Equal(CapsuleOpenStatus.KeyRevoked, result.Status);
    }

    [Fact]
    public async Task Open_KeyNotOnCdn_Fails()
    {
        var path = _fixture.WriteCapsule(_dir);

        var result = await _runtime.OpenAsync(path, Prompt(true), CancellationToken.None);

        Assert.Equal(CapsuleOpenStatus.KeyNotFound, result.Status);
    }

    [Fact]
    public async Task Open_OfflineWithNoCache_FailsWithNetworkError()
    {
        // Offline revocation behavior: reject with a warning, never silently allow.
        _cdn.FailWithNetworkError = true;
        var path = _fixture.WriteCapsule(_dir);

        var result = await _runtime.OpenAsync(path, Prompt(true), CancellationToken.None);

        Assert.Equal(CapsuleOpenStatus.NetworkError, result.Status);
    }

    [Fact]
    public async Task Open_OfflineWithActiveCache_SucceedsFromCache()
    {
        _trustStore.Trust(_fixture.Fingerprint, "Test Creator");
        _trustStore.CacheMetadata(_fixture.Fingerprint, _fixture.MakeKeyMetadata(active: true));
        _cdn.FailWithNetworkError = true;
        var path = _fixture.WriteCapsule(_dir);

        var result = await _runtime.OpenAsync(path, Prompt(true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _cdn.RequestCount);
    }

    [Fact]
    public async Task Open_CapabilityNotGranted_Denied()
    {
        _cdn.SetKey(_fixture.MakeKeyMetadata(active: true, CapsuleCapability.WebViewLocalContent));
        var path = Path.Combine(_dir, "caps.spectralis");
        File.WriteAllBytes(path, _fixture.BuildCapsuleBytes(
            capabilities: [CapsuleCapability.WebViewLocalContent, CapsuleCapability.WebViewNetworkAccess]));

        var result = await _runtime.OpenAsync(path, Prompt(true), CancellationToken.None);

        Assert.Equal(CapsuleOpenStatus.CapabilityDenied, result.Status);
        Assert.Contains(CapsuleCapability.WebViewNetworkAccess, result.ErrorMessage);
    }

    [Fact]
    public async Task Open_GrantedCapabilities_Succeed()
    {
        _cdn.SetKey(_fixture.MakeKeyMetadata(active: true, CapsuleCapability.WebViewLocalContent));
        var path = Path.Combine(_dir, "caps-ok.spectralis");
        File.WriteAllBytes(path, _fixture.BuildCapsuleBytes(
            capabilities: [CapsuleCapability.WebViewLocalContent]));

        var result = await _runtime.OpenAsync(path, Prompt(true), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Open_InvalidFile_FailsWithoutNetworkCalls()
    {
        var path = Path.Combine(_dir, "junk.spectralis");
        File.WriteAllBytes(path, new byte[256]);

        var result = await _runtime.OpenAsync(path, Prompt(true), CancellationToken.None);

        Assert.Equal(CapsuleOpenStatus.InvalidFile, result.Status);
        Assert.Equal(0, _cdn.RequestCount);
    }
}

public sealed class CreatorTrustStoreTests : IDisposable
{
    private readonly string _dir;

    public CreatorTrustStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"spectralis-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void TrustAndMetadata_PersistAcrossReload()
    {
        var path = Path.Combine(_dir, "store.json");
        var store = new CreatorTrustStore(path);
        store.Trust("abc123", "Creator");
        store.CacheMetadata("abc123", new CreatorKeyMetadata { Fingerprint = "abc123", DisplayName = "Creator" });

        var reloaded = new CreatorTrustStore(path);
        reloaded.Load();

        Assert.True(reloaded.IsTrusted("ABC123")); // case-insensitive
        Assert.NotNull(reloaded.GetCachedMetadata("abc123"));
    }

    [Fact]
    public void Revoke_RemovesTrust()
    {
        var store = new CreatorTrustStore(Path.Combine(_dir, "store.json"));
        store.Trust("abc", "Creator");
        store.Revoke("abc");

        Assert.False(store.IsTrusted("abc"));
    }

    [Fact]
    public void Load_CorruptFile_StartsEmpty()
    {
        var path = Path.Combine(_dir, "store.json");
        File.WriteAllText(path, "{ corrupt");
        var store = new CreatorTrustStore(path);

        store.Load();

        Assert.Empty(store.Entries);
    }
}
