using System.Buffers.Binary;
using System.Text;

namespace Spectralis.Core.Podcasts;

/// <summary>
/// Reads embedded chapters from an MP4/M4A/M4B container. v1 parses the Nero
/// <c>moov/udta/chpl</c> box (the format almost every real <c>.m4b</c> audiobook uses).
/// The QuickTime <c>tref/chap</c> text-track form is a documented follow-up.
/// </summary>
public static class Mp4ChapterReader
{
    // moov is small even for audiobooks (metadata + maybe cover art); refuse anything absurd.
    private const long MaxMoovBytes = 96L * 1024 * 1024;

    public static IReadOnlyList<Chapter> Read(string audioPath)
    {
        try
        {
            using var stream = File.OpenRead(audioPath);
            var moov = ReadTopLevelBox(stream, "moov");
            return moov is null ? Array.Empty<Chapter>() : ParseMoov(moov);
        }
        catch
        {
            return Array.Empty<Chapter>();
        }
    }

    public static IReadOnlyList<Chapter> ParseMoov(ReadOnlySpan<byte> moov)
    {
        var udta = FindChildBox(moov, "udta");
        if (udta.IsEmpty)
        {
            return Array.Empty<Chapter>();
        }

        var chpl = FindChildBox(udta, "chpl");
        return chpl.IsEmpty ? Array.Empty<Chapter>() : ParseChpl(chpl);
    }

    public static IReadOnlyList<Chapter> ParseChpl(ReadOnlySpan<byte> chpl)
    {
        if (chpl.Length < 5)
        {
            return Array.Empty<Chapter>();
        }

        var version = chpl[0];
        var offset = 4; // version (1) + flags (3)

        // Nero version 1 carries a 4-byte reserved field before the count.
        if (version == 1)
        {
            offset += 4;
        }

        if (offset >= chpl.Length)
        {
            return Array.Empty<Chapter>();
        }

        int count = chpl[offset];
        offset += 1;

        var parsed = new List<(TimeSpan Start, string Title)>();
        while (offset + 9 <= chpl.Length && (count == 0 || parsed.Count < count))
        {
            var startTicks = BinaryPrimitives.ReadUInt64BigEndian(chpl.Slice(offset, 8));
            offset += 8;

            int titleLen = chpl[offset];
            offset += 1;

            if (offset + titleLen > chpl.Length)
            {
                break;
            }

            var title = Encoding.UTF8.GetString(chpl.Slice(offset, titleLen)).Trim();
            offset += titleLen;

            // chpl timestamps are in 100-ns units — identical to TimeSpan ticks.
            parsed.Add((new TimeSpan((long)Math.Min(startTicks, long.MaxValue)), title));
        }

        if (parsed.Count == 0)
        {
            return Array.Empty<Chapter>();
        }

        parsed.Sort((a, b) => a.Start.CompareTo(b.Start));
        var result = new List<Chapter>(parsed.Count);
        for (var i = 0; i < parsed.Count; i++)
        {
            TimeSpan? end = i + 1 < parsed.Count ? parsed[i + 1].Start : null;
            var title = string.IsNullOrWhiteSpace(parsed[i].Title) ? $"Chapter {i + 1}" : parsed[i].Title;
            result.Add(new Chapter(parsed[i].Start, end, title));
        }

        return result;
    }

    /// <summary>Scans the top level of an ISO-BMFF file for a box and returns its payload.</summary>
    private static byte[]? ReadTopLevelBox(Stream stream, string type)
    {
        Span<byte> header = stackalloc byte[16];
        while (true)
        {
            var boxStart = stream.Position;
            if (!ReadExactly(stream, header[..8]))
            {
                return null;
            }

            long size = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
            var boxType = Encoding.ASCII.GetString(header[4..8]);
            var headerLen = 8;

            if (size == 1)
            {
                if (!ReadExactly(stream, header[8..16]))
                {
                    return null;
                }

                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..16]);
                headerLen = 16;
            }
            else if (size == 0)
            {
                size = stream.Length - boxStart;
            }

            var payloadLen = size - headerLen;
            if (payloadLen < 0 || boxStart + size > stream.Length + 1)
            {
                return null;
            }

            if (boxType == type)
            {
                if (payloadLen > MaxMoovBytes)
                {
                    return null;
                }

                var payload = new byte[payloadLen];
                return ReadExactly(stream, payload) ? payload : null;
            }

            stream.Seek(boxStart + size, SeekOrigin.Begin);
        }
    }

    /// <summary>Finds a direct child box within an already-buffered container payload.</summary>
    private static ReadOnlySpan<byte> FindChildBox(ReadOnlySpan<byte> container, string type)
    {
        var offset = 0;
        while (offset + 8 <= container.Length)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(container.Slice(offset, 4));
            var boxType = Encoding.ASCII.GetString(container.Slice(offset + 4, 4));
            var headerLen = 8;

            if (size == 1)
            {
                if (offset + 16 > container.Length)
                {
                    break;
                }

                size = (long)BinaryPrimitives.ReadUInt64BigEndian(container.Slice(offset + 8, 8));
                headerLen = 16;
            }
            else if (size == 0)
            {
                size = container.Length - offset;
            }

            if (size < headerLen || offset + size > container.Length)
            {
                break;
            }

            if (boxType == type)
            {
                return container.Slice(offset + headerLen, (int)(size - headerLen));
            }

            offset += (int)size;
        }

        return ReadOnlySpan<byte>.Empty;
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer[read..]);
            if (n == 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }
}
