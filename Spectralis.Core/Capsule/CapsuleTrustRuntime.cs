namespace Spectralis.Core.Capsule;

public enum CapsuleOpenStatus
{
    Success,
    InvalidFile,
    SignatureInvalid,
    KeyNotFound,
    KeyRevoked,
    CapabilityDenied,
    UserDenied,
    NetworkError,
}

public sealed class CapsuleOpenResult
{
    public CapsuleOpenStatus Status { get; init; }
    public CapsulePackage? Package { get; init; }
    public CreatorKeyMetadata? KeyMetadata { get; init; }
    public string? ErrorMessage { get; init; }

    public bool IsSuccess => Status == CapsuleOpenStatus.Success;

    public static CapsuleOpenResult Ok(CapsulePackage package, CreatorKeyMetadata key) =>
        new() { Status = CapsuleOpenStatus.Success, Package = package, KeyMetadata = key };

    public static CapsuleOpenResult Fail(CapsuleOpenStatus status, string message) =>
        new() { Status = status, ErrorMessage = message };
}

/// <summary>
/// Full open pipeline for .spectralis capsules: signature verification (every
/// load), CDN key validation with local cache, revocation enforcement (offline
/// = reject with warning, never silently allow), capability intersection, and
/// the one-time trust prompt for unknown creators.
/// </summary>
public sealed class CapsuleTrustRuntime : IDisposable
{
    private readonly CreatorTrustStore _trustStore;
    private readonly CapsuleCdnClient _cdnClient;

    public CapsuleTrustRuntime(CreatorTrustStore trustStore, CapsuleCdnClient? cdnClient = null)
    {
        _trustStore = trustStore;
        _cdnClient = cdnClient ?? new CapsuleCdnClient();
    }

    public async Task<CapsuleOpenResult> OpenAsync(
        string path,
        Func<CapsuleTrustContext, Task<bool>> trustPrompt,
        CancellationToken cancellationToken)
    {
        // 1. Parse and verify the Ed25519 signature.
        CapsulePackage package;
        try
        {
            package = CapsuleReader.Read(path);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("signature", StringComparison.OrdinalIgnoreCase))
        {
            return CapsuleOpenResult.Fail(CapsuleOpenStatus.SignatureInvalid, ex.Message);
        }
        catch (InvalidDataException ex)
        {
            return CapsuleOpenResult.Fail(CapsuleOpenStatus.InvalidFile, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CapsuleOpenResult.Fail(CapsuleOpenStatus.InvalidFile, $"Could not read capsule: {ex.Message}");
        }

        var fingerprint = package.Fingerprint;

        // 2. Resolve CDN key metadata. The local cache (which doubles as the
        // revocation cache) is consulted first; a cached entry that would deny
        // is refreshed before the denial is final.
        var keyMeta = _trustStore.GetCachedMetadata(fingerprint);
        var keyMetaFromCache = keyMeta is not null;
        if (keyMeta is null)
        {
            try
            {
                keyMeta = await _cdnClient.FetchCreatorKeyAsync(fingerprint, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CapsuleOpenResult.Fail(
                    CapsuleOpenStatus.NetworkError,
                    $"Could not reach CDN to verify the creator key: {ex.Message}");
            }
        }

        if (keyMeta is null)
        {
            return CapsuleOpenResult.Fail(
                CapsuleOpenStatus.KeyNotFound,
                "This capsule's signing key is not registered on the CDN.");
        }

        if (keyMetaFromCache && !keyMeta.IsActive)
        {
            try
            {
                keyMeta = await _cdnClient.FetchCreatorKeyAsync(fingerprint, cancellationToken);
                keyMetaFromCache = false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CapsuleOpenResult.Fail(
                    CapsuleOpenStatus.NetworkError,
                    $"Could not refresh cached creator key metadata from the CDN: {ex.Message}");
            }

            if (keyMeta is null)
            {
                return CapsuleOpenResult.Fail(
                    CapsuleOpenStatus.KeyNotFound,
                    "This capsule's signing key is not registered on the CDN.");
            }
        }

        if (!keyMeta.IsActive)
        {
            return CapsuleOpenResult.Fail(
                CapsuleOpenStatus.KeyRevoked,
                $"The creator key for '{keyMeta.DisplayName}' has been revoked.");
        }

        // 3. Capability intersection: declared ∩ CDN-granted must equal declared.
        var denied = package.Manifest.Capabilities
            .Where(capability => !keyMeta.AllowedCapabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (denied.Count > 0 && keyMetaFromCache)
        {
            try
            {
                keyMeta = await _cdnClient.FetchCreatorKeyAsync(fingerprint, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CapsuleOpenResult.Fail(
                    CapsuleOpenStatus.NetworkError,
                    $"Could not refresh cached creator key metadata from the CDN: {ex.Message}");
            }

            if (keyMeta is null)
            {
                return CapsuleOpenResult.Fail(
                    CapsuleOpenStatus.KeyNotFound,
                    "This capsule's signing key is not registered on the CDN.");
            }

            if (!keyMeta.IsActive)
            {
                return CapsuleOpenResult.Fail(
                    CapsuleOpenStatus.KeyRevoked,
                    $"The creator key for '{keyMeta.DisplayName}' has been revoked.");
            }

            denied = package.Manifest.Capabilities
                .Where(capability => !keyMeta.AllowedCapabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        if (denied.Count > 0)
        {
            return CapsuleOpenResult.Fail(
                CapsuleOpenStatus.CapabilityDenied,
                $"Capsule requests capabilities not granted to this creator: {string.Join(", ", denied)}");
        }

        // 4. Cache the validated metadata (revocation cache update).
        _trustStore.CacheMetadata(fingerprint, keyMeta);

        // 5. One-time trust prompt for unknown creators.
        if (!_trustStore.IsTrusted(fingerprint))
        {
            var trustContext = new CapsuleTrustContext
            {
                Creator = keyMeta,
                RequestedCapabilities = package.Manifest.Capabilities,
                ContentTags = package.Manifest.Story.Tags,
            };
            var trusted = await trustPrompt(trustContext);
            if (!trusted)
            {
                return CapsuleOpenResult.Fail(CapsuleOpenStatus.UserDenied, "User declined to trust this creator.");
            }

            _trustStore.Trust(fingerprint, keyMeta.DisplayName);
        }

        return CapsuleOpenResult.Ok(package, keyMeta);
    }

    public void Dispose() => _cdnClient.Dispose();
}
