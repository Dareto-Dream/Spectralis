using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Spectralis.Core.Audio.Dj;

/// <summary>Reads NI's moov/udta/stem JSON without scanning or loading audio data.</summary>
internal static class Mp4StemMetadata
{
    internal static IReadOnlyList<string?> ReadNames(string path, CancellationToken ct)
    {
        try
        {
            using var file = File.OpenRead(path);
            var remainingBoxes = 8192;
            return ReadBoxes(file, 0, file.Length, 0, ref remainingBoxes, ct) ?? [];
        }
        catch (IOException) { return []; }
        catch (JsonException) { return []; }
    }

    private static IReadOnlyList<string?>? ReadBoxes(FileStream file, long start, long end, int depth, ref int remaining, CancellationToken ct)
    {
        Span<byte> header = stackalloc byte[16];
        for (var position = start; end - position >= 8 && remaining-- > 0;)
        {
            ct.ThrowIfCancellationRequested();
            file.Position = position;
            file.ReadExactly(header[..8]);
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.ASCII.GetString(header[4..8]);
            var headerSize = 8;
            if (size == 1)
            {
                if (end - position < 16) return null;
                file.ReadExactly(header[8..]);
                size = BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
                headerSize = 16;
            }
            else if (size == 0) size = (ulong)(end - position);
            if (size < (ulong)headerSize || size > (ulong)(end - position)) return null;
            var next = position + (long)size;
            if (depth == 2 && type == "stem")
            {
                var length = (long)size - headerSize;
                if (length is < 2 or > 1024 * 1024) return null;
                var bytes = new byte[(int)length];
                file.ReadExactly(bytes);
                using var json = JsonDocument.Parse(bytes);
                if (json.RootElement.ValueKind != JsonValueKind.Object ||
                    !json.RootElement.TryGetProperty("stems", out var stems) || stems.ValueKind != JsonValueKind.Array ||
                    stems.GetArrayLength() is < 1 or > 64) return null;
                return stems.EnumerateArray().Select(stem => stem.ValueKind == JsonValueKind.Object &&
                    stem.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString())
                        ? name.GetString()!.Trim() : null).ToArray();
            }
            if (depth == 0 && type == "moov" || depth == 1 && type == "udta")
            {
                var found = ReadBoxes(file, position + headerSize, next, depth + 1, ref remaining, ct);
                if (found is not null) return found;
            }
            position = next;
        }
        return null;
    }
}
