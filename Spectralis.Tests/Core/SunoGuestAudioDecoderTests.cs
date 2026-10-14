using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spectralis.Core.Integrations;
using Xunit;

namespace Spectralis.Tests.Core;

public class SunoGuestAudioDecoderTests
{
    private const string ClipId = "652aa712-14cf-4d6b-9d4a-187b180bc33c";
    private const string Token = "synthetic-guest-token";
    private static readonly Uri Media = new("https://example.com/audio.m4a");
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    // Carry from the low 64 bits must reach the upper half of the 128-bit counter.
    private static readonly byte[] Iv = Convert.FromHexString("0102030405060708ffffffffffffffff");

    [Fact]
    public async Task Decode_RequestsFreshRightsAndHandlesPartialNetworkBlocks()
    {
        var clear = Mp4();
        var encrypted = EncryptCtr(clear);
        var requests = 0;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Null(request.Headers.Authorization);
            if (request.Method == HttpMethod.Post)
            {
                requests++;
                Assert.Equal("https://studio-api-prod.suno.com/api/mango/rights", request.RequestUri!.AbsoluteUri);
                Assert.Equal("https://suno.com", request.Headers.GetValues("Origin").Single());
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var parameters = body.RootElement.GetProperty("content_params");
                Assert.Equal(ClipId, parameters.GetProperty("content_id").GetString());
                Assert.Equal("clip", parameters.GetProperty("content_type").GetString());
                return Json(License(Token + requests));
            }
            Assert.Equal(Media, request.RequestUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FragmentedStream(encrypted))
            };
        }));
        var decoder = new SunoGuestAudioDecoder(http);
        for (var i = 0; i < 2; i++)
        {
            using var output = new MemoryStream();
            await decoder.DownloadAndDecryptAsync(ClipId.ToUpperInvariant(), Media, output, default);
            Assert.Equal(0, output.Position);
            Assert.Equal(clear, output.ToArray());
        }
        Assert.Equal(2, requests);
    }

    [Fact]
    public void Unwrap_AuthenticatesBothClipIdAndExactGuestToken()
    {
        var license = License();
        var material = SunoGuestAudioDecoder.UnwrapLicense(license, ClipId);
        Assert.Equal(Key, material.Key);
        Assert.Equal(Iv, material.Iv);
        Assert.Throws<AuthenticationTagMismatchException>(() => SunoGuestAudioDecoder.UnwrapLicense(license, Guid.NewGuid().ToString()));
        Assert.Throws<AuthenticationTagMismatchException>(() => SunoGuestAudioDecoder.UnwrapLicense(
            license with { GuestToken = Token + "wrong" }, ClipId));
    }

    [Fact]
    public void Unwrap_RejectsTruncatedFieldsAndInvalidMaterialLengths()
    {
        Assert.Throws<InvalidDataException>(() => SunoGuestAudioDecoder.UnwrapLicense(
            License() with { Key = Convert.ToBase64String(new byte[27]) }, ClipId));
        Assert.Throws<InvalidDataException>(() => SunoGuestAudioDecoder.UnwrapLicense(
            License() with { Iv = Wrap(new byte[15], Token) }, ClipId));
        Assert.Throws<InvalidDataException>(() => SunoGuestAudioDecoder.UnwrapLicense(
            License() with { Key = Wrap(new byte[17], Token) }, ClipId));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Decode_DoesNotFetchMediaWhenGuestRightsAreDenied(HttpStatusCode status)
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            return Task.FromResult(new HttpResponseMessage(status));
        }));
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new SunoGuestAudioDecoder(http).DownloadAndDecryptAsync(ClipId, Media, output, default));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task Decode_RejectsMissingGuestToken()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(new { key = "a", iv = "b" }))));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SunoGuestAudioDecoder(http).DownloadAndDecryptAsync(ClipId, Media, output, default));
    }

    [Fact]
    public async Task Decode_RejectsIncorrectMediaAndPropagatesCancellation()
    {
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Post ? Json(License()) : new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[64]) })));
        using var output = new MemoryStream();
        var decoder = new SunoGuestAudioDecoder(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => decoder.DownloadAndDecryptAsync(ClipId, Media, output, default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            decoder.DownloadAndDecryptAsync(ClipId, Media, output, cancellation.Token));
    }

    [Fact]
    public void ValidateMp4_AcceptsExtendedAndToEndBoxes()
    {
        using var output = new MemoryStream();
        output.Write(Box("ftyp", new byte[8]));
        output.Write(Convert.FromHexString("000000016d6f6f760000000000000010"));
        output.Write(Convert.FromHexString("000000006d646174"));
        output.Write(new byte[19]);
        SunoGuestAudioDecoder.ValidateMp4(output);
    }

    [Theory]
    [InlineData("0000001066747970")] // truncated box
    [InlineData("0000000866747970000000086d646174")] // missing moov
    [InlineData("0000000766747970")] // undersized box
    [InlineData("0000000866747970000000016d6f6f7600000000")] // truncated extended header
    [InlineData("0000000866747970000000016d6f6f76ffffffffffffffff")] // huge size
    [InlineData("0000000866747970000000086d6f6f76000000086d64617401")] // partial trailing header
    public void ValidateMp4_RejectsCorruptOrIncompleteContainers(string hex)
    {
        using var data = new MemoryStream(Convert.FromHexString(hex));
        Assert.Throws<InvalidDataException>(() => SunoGuestAudioDecoder.ValidateMp4(data));
    }

    private static SunoGuestAudioDecoder.GuestLicense License(string token = Token) =>
        new(Wrap(Key, token), Wrap(Iv, token), token);

    private static string Wrap(byte[] value, string token)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var packed = new byte[12 + value.Length + 16];
        nonce.CopyTo(packed, 0);
        using var gcm = new AesGcm(SHA256.HashData(Encoding.UTF8.GetBytes(token)), 16);
        gcm.Encrypt(nonce, value, packed.AsSpan(12, value.Length), packed.AsSpan(12 + value.Length), Encoding.UTF8.GetBytes(ClipId));
        return Convert.ToBase64String(packed);
    }

    // Independent ECB-based CTR fixture, rather than encrypting with the decoder's cipher.
    private static byte[] EncryptCtr(byte[] clear)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        var counter = Iv.ToArray();
        var encrypted = new byte[clear.Length];
        for (var offset = 0; offset < clear.Length; offset += 16)
        {
            var block = aes.EncryptEcb(counter, PaddingMode.None);
            for (var i = 0; i < Math.Min(16, clear.Length - offset); i++)
                encrypted[offset + i] = (byte)(clear[offset + i] ^ block[i]);
            for (var i = 15; i >= 0 && ++counter[i] == 0; i--) { }
        }
        return encrypted;
    }

    private static byte[] Mp4() => Box("ftyp", new byte[8])
        .Concat(Box("moov", new byte[13]))
        .Concat(Box("mdat", Enumerable.Range(0, 300013).Select(i => (byte)i).ToArray())).ToArray();

    private static byte[] Box(string type, byte[] payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return respond(request, cancellationToken);
        }
    }

    private sealed class FragmentedStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 997)], cancellationToken);
    }
}
