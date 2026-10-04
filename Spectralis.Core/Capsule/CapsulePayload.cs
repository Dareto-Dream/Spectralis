using System.Collections.Concurrent;
using System.IO.Compression;

namespace Spectralis.Core.Capsule;

/// <summary>
/// The zip payload of a verified capsule, opened once and reused. Every read used to build a fresh
/// <see cref="ZipArchive"/> (re-parsing the whole central directory), which made loading a capsule with
/// many assets quadratic, and copied each entry twice on the way out. Archives aren't thread-safe, so
/// readers borrow one from a small pool: sequential reads share a single archive, concurrent reads each
/// get their own instead of queueing behind a lock.
/// </summary>
internal sealed class CapsulePayload
{
    private readonly byte[] _bytes;
    private readonly ConcurrentBag<ZipArchive> _idle = new();
    private string[]? _names;

    public CapsulePayload(byte[] bytes) => _bytes = bytes;

    /// <summary>Runs <paramref name="action"/> against a pooled archive. An archive that threw is discarded, not reused.</summary>
    public T Use<T>(Func<ZipArchive, T> action)
    {
        if (!_idle.TryTake(out var zip))
        {
            zip = new ZipArchive(new MemoryStream(_bytes, writable: false), ZipArchiveMode.Read);
        }

        var result = action(zip);
        _idle.Add(zip);
        return result;
    }

    /// <summary>Entry names in archive order. Computed once.</summary>
    public IReadOnlyList<string> EntryNames() =>
        _names ??= Use(static zip => zip.Entries.Select(static entry => entry.FullName).ToArray());

    /// <summary>
    /// Reads one entry straight into an exactly-sized array (no intermediate stream, no second copy).
    /// Null if it's missing or its declared size exceeds <paramref name="maxBytes"/>. An entry that
    /// decompresses to a different size than its header declares is corrupt and throws, so a lying
    /// header can't make us over-read or hand back a short buffer.
    /// </summary>
    public byte[]? TryReadEntry(string name, long maxBytes) =>
        Use(zip =>
        {
            var entry = zip.GetEntry(name);
            if (entry is null || entry.Length > maxBytes)
            {
                return null;
            }

            var result = new byte[entry.Length];
            using var stream = entry.Open();
            try
            {
                stream.ReadExactly(result);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("Capsule entry is shorter than its header says.", ex);
            }

            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException("Capsule entry is longer than its header says.");
            }

            return result;
        });
}
