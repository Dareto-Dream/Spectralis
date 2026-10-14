using System.Buffers.Binary;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Spectralis.Core.Integrations;

/// <summary>Native port of the Suno guest decoder: fresh rights, GCM key unwrap, CTR media decode.</summary>
public sealed class SunoGuestAudioDecoder(HttpClient http)
{
    private const string RightsUrl = "https://studio-api-prod.suno.com/api/mango/rights";

    public async Task DownloadAndDecryptAsync(
        string clipId, Uri audioUrl, Stream output, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(clipId, "D", out var id))
            throw new ArgumentException("A Suno clip UUID is required.", nameof(clipId));
        clipId = id.ToString("D");
        if (audioUrl.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Suno media must use HTTPS.", nameof(audioUrl));
        if (!output.CanRead || !output.CanWrite || !output.CanSeek)
            throw new ArgumentException("The decoded output must be readable, writable, and seekable.", nameof(output));

        var license = await RequestRightsAsync(clipId, cancellationToken);
        var (key, iv) = UnwrapLicense(license, clipId);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            using var request = new HttpRequestMessage(HttpMethod.Get, audioUrl);
            request.Headers.Referrer = new Uri("https://suno.com/");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);

            // SIC is AES-CTR with the entire 128-bit counter, matching WebCrypto length:128.
            // One cipher spans every network read, including reads that end inside a block.
            var cipher = new BufferedBlockCipher(new SicBlockCipher(new AesEngine()));
            cipher.Init(false, new ParametersWithIV(new KeyParameter(key), iv));
            var encrypted = new byte[128 * 1024];
            var clear = new byte[encrypted.Length + 16];
            output.SetLength(0);
            output.Position = 0;
            int count;
            while ((count = await source.ReadAsync(encrypted, timeout.Token)) > 0)
            {
                var written = cipher.ProcessBytes(encrypted, 0, count, clear, 0);
                await output.WriteAsync(clear.AsMemory(0, written), timeout.Token);
            }
            var final = cipher.DoFinal(clear, 0);
            await output.WriteAsync(clear.AsMemory(0, final), timeout.Token);
            await output.FlushAsync(timeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateMp4(output);
            output.Position = 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(iv);
        }
    }

    private async Task<GuestLicense> RequestRightsAsync(string clipId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Post, RightsUrl);
        request.Headers.Add("Origin", "https://suno.com");
        request.Headers.Referrer = new Uri("https://suno.com/");
        request.Content = JsonContent.Create(new { content_params = new { content_id = clipId, content_type = "clip" } });
        using var response = await http.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Suno guest rights returned HTTP {(int)response.StatusCode}. The song must be available for guest playback.",
                null, response.StatusCode);
        var license = await response.Content.ReadFromJsonAsync<GuestLicense>(cancellationToken: timeout.Token);
        if (license is null || string.IsNullOrWhiteSpace(license.Key) ||
            string.IsNullOrWhiteSpace(license.Iv) || string.IsNullOrWhiteSpace(license.GuestToken))
            throw new InvalidDataException("Suno guest rights did not include key, iv, and glt.");
        return license;
    }

    internal static (byte[] Key, byte[] Iv) UnwrapLicense(GuestLicense license, string clipId)
    {
        // Hash the exact fresh guest JWT, not a captured token or its decoded payload.
        var wrappingKey = SHA256.HashData(Encoding.UTF8.GetBytes(license.GuestToken));
        var aad = Encoding.UTF8.GetBytes(clipId);
        byte[]? key = null;
        byte[]? iv = null;
        try
        {
            using var gcm = new AesGcm(wrappingKey, 16);
            key = Unwrap(license.Key, gcm, aad);
            iv = Unwrap(license.Iv, gcm, aad);
            if (key.Length is not (16 or 24 or 32) || iv.Length != 16)
                throw new InvalidDataException("Suno returned an invalid AES key or CTR IV length.");
            return (key, iv);
        }
        catch
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (iv is not null) CryptographicOperations.ZeroMemory(iv);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    private static byte[] Unwrap(string encoded, AesGcm gcm, byte[] aad)
    {
        var packed = Convert.FromBase64String(encoded);
        if (packed.Length < 28)
            throw new InvalidDataException("Suno license field is missing its nonce or authentication tag.");
        var clear = new byte[packed.Length - 28];
        // Each base64 field is nonce (12 bytes), ciphertext, then GCM tag (16 bytes).
        gcm.Decrypt(packed.AsSpan(0, 12), packed.AsSpan(12, clear.Length), packed.AsSpan(packed.Length - 16), clear, aad);
        return clear;
    }

    internal static void ValidateMp4(Stream data)
    {
        data.Position = 0;
        Span<byte> header = stackalloc byte[16];
        if (data.Length < 16)
            throw new InvalidDataException("Decrypted Suno audio has no MP4 header.");
        var hasMoov = false;
        var hasMdat = false;
        while (data.Position < data.Length)
        {
            var offset = data.Position;
            if (data.Length - offset < 8)
                throw new InvalidDataException("Decrypted Suno audio ends inside an MP4 box header.");
            data.ReadExactly(header[..8]);
            if (offset == 0 && !header.Slice(4, 4).SequenceEqual("ftyp"u8))
                throw new InvalidDataException("Decrypted Suno audio has no MP4 ftyp header.");
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var headerSize = 8;
            if (size == 1)
            {
                if (data.Length - data.Position < 8)
                    throw new InvalidDataException("Truncated extended MP4 box header.");
                data.ReadExactly(header[8..]);
                size = BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
                headerSize = 16;
            }
            else if (size == 0)
                size = (ulong)(data.Length - offset);
            if (size < (ulong)headerSize || size > (ulong)(data.Length - offset))
                throw new InvalidDataException("Invalid decrypted Suno MP4 box sizes.");
            hasMoov |= header.Slice(4, 4).SequenceEqual("moov"u8);
            hasMdat |= header.Slice(4, 4).SequenceEqual("mdat"u8);
            data.Position = offset + (long)size;
        }
        if (!hasMoov || !hasMdat)
            throw new InvalidDataException("Decrypted Suno MP4 is missing moov or mdat.");
    }

    internal sealed record GuestLicense(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("iv")] string Iv,
        [property: JsonPropertyName("glt")] string GuestToken);
}
