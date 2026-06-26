namespace Spectralis;

internal enum CapsuleOpenStatus
{
    Success,
    InvalidFile,
    SignatureInvalid,
    KeyNotFound,
    KeyRevoked,
    CapabilityDenied,
    UserDenied,
    NetworkError
}

internal sealed class CapsuleOpenResult
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

internal sealed class CapsuleTrustRuntime : IDisposable
{
    private readonly CreatorTrustStore trustStore;
    private readonly CapsuleCdnClient cdnClient;

    public CapsuleTrustRuntime(CreatorTrustStore trustStore)
    {
        this.trustStore = trustStore;
        cdnClient = new CapsuleCdnClient();
    }

    public async Task<CapsuleOpenResult> OpenAsync(
        string path,
        Func<CreatorKeyMetadata, bool> trustPrompt,
        CancellationToken cancellationToken)
    {
        // 1. Parse and verify signature
        CapsulePackage package;
        try
        {
            package = CapsuleReader.Read(path);
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

        // 2. Fetch CDN key metadata. Cached entries avoid blocking on every open,
        // but stale entries must be refreshed before denying a capability or inactive key.
        CreatorKeyMetadata? keyMeta = trustStore.GetCachedMetadata(fingerprint);
        var keyMetaFromCache = keyMeta is not null;
        if (keyMeta is null)
        {
            try
            {
                keyMeta = await cdnClient.FetchCreatorKeyAsync(fingerprint, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CapsuleOpenResult.Fail(CapsuleOpenStatus.NetworkError,
                    $"Could not reach CDN to verify the creator key: {ex.Message}");
            }
        }

        if (keyMeta is null)
            return CapsuleOpenResult.Fail(CapsuleOpenStatus.KeyNotFound,
                "This capsule's signing key is not registered on the CDN.");

        if (keyMetaFromCache && !keyMeta.IsActive)
        {
            try
            {
                keyMeta = await cdnClient.FetchCreatorKeyAsync(fingerprint, cancellationToken);
                keyMetaFromCache = false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CapsuleOpenResult.Fail(CapsuleOpenStatus.NetworkError,
                    $"Could not refresh cached creator key metadata from the CDN: {ex.Message}");
            }
        }

        if (keyMeta is null)
            return CapsuleOpenResult.Fail(CapsuleOpenStatus.KeyNotFound,
                "This capsule's signing key is not registered on the CDN.");

        if (!keyMeta.IsActive)
            return CapsuleOpenResult.Fail(CapsuleOpenStatus.KeyRevoked,
                $"The creator key for '{keyMeta.DisplayName}' has been revoked.");

        // 3. Intersect requested capabilities with CDN-allowed capabilities
        var denied = package.Manifest.Capabilities
            .Where(c => !keyMeta.AllowedCapabilities.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (denied.Count > 0 && keyMetaFromCache)
        {
            try
            {
                keyMeta = await cdnClient.FetchCreatorKeyAsync(fingerprint, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CapsuleOpenResult.Fail(CapsuleOpenStatus.NetworkError,
                    $"Could not refresh cached creator key metadata from the CDN: {ex.Message}");
            }

            if (keyMeta is null)
                return CapsuleOpenResult.Fail(CapsuleOpenStatus.KeyNotFound,
                    "This capsule's signing key is not registered on the CDN.");

            if (!keyMeta.IsActive)
                return CapsuleOpenResult.Fail(CapsuleOpenStatus.KeyRevoked,
                    $"The creator key for '{keyMeta.DisplayName}' has been revoked.");

            denied = package.Manifest.Capabilities
                .Where(c => !keyMeta.AllowedCapabilities.Contains(c, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        if (denied.Count > 0)
            return CapsuleOpenResult.Fail(CapsuleOpenStatus.CapabilityDenied,
                $"Capsule requests capabilities not granted to this creator: {string.Join(", ", denied)}");

        // 4. Cache updated metadata
        trustStore.CacheMetadata(fingerprint, keyMeta);

        // 5. Check trust store; prompt if unknown
        if (!trustStore.IsTrusted(fingerprint))
        {
            var trusted = trustPrompt(keyMeta);
            if (!trusted)
            {
                package.Dispose();
                return CapsuleOpenResult.Fail(CapsuleOpenStatus.UserDenied, "User declined to trust this creator.");
            }

            trustStore.Trust(fingerprint, keyMeta.DisplayName);
        }

        return CapsuleOpenResult.Ok(package, keyMeta);
    }

    public void Dispose() => cdnClient.Dispose();
}
