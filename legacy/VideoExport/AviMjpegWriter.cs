using System.IO;
using System.Text;

namespace Spectralis;

// Writes a minimal RIFF/AVI file with an MJPEG video stream and 16-bit PCM audio stream.
internal sealed class AviMjpegWriter : IDisposable
{
    private readonly FileStream _file;
    private readonly BinaryWriter _bw;
    private readonly int _fps, _width, _height, _sampleRate, _channels;

    private long _riffSizePos;
    private long _hdrlSizePos;
    private long _moviListPos;   // position of the 'LIST' fourcc for movi
    private long _moviSizePos;   // position of the LIST size field for movi
    private long _avihTotalFramesPos;

    private readonly List<(long FilePos, bool IsVideo, int DataSize)> _index = new();
    private int _videoFrameCount;
    private bool _disposed;

    public AviMjpegWriter(string path, int width, int height, int fps, int sampleRate, int channels)
    {
        _fps = fps; _width = width; _height = height;
        _sampleRate = sampleRate; _channels = channels;
        _file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 131072);
        _bw = new BinaryWriter(_file, Encoding.ASCII);
        WriteHeader();
    }

    public void WriteVideoFrame(byte[] jpegData)
    {
        var pos = _file.Position;
        WriteChunk("00dc"u8, jpegData);
        _index.Add((pos, true, jpegData.Length));
        _videoFrameCount++;
    }

    public void WriteAudioBlock(byte[] pcmData)
    {
        var pos = _file.Position;
        WriteChunk("01wb"u8, pcmData);
        _index.Add((pos, false, pcmData.Length));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { FinalizeFile(); } catch { }
        _bw.Dispose();
    }

    private void FinalizeFile()
    {
        _bw.Flush();
        var moviEnd = _file.Position;

        // Patch movi LIST size = everything from after the 'movi' size field to end
        Patch32(_moviSizePos, (uint)(moviEnd - _moviSizePos - 4));

        WriteIdx1();

        _bw.Flush();
        var fileEnd = _file.Position;

        Patch32(_riffSizePos, (uint)(fileEnd - 8));
        Patch32(_avihTotalFramesPos, (uint)_videoFrameCount);
        _bw.Flush();
    }

    private void WriteHeader()
    {
        // RIFF 'AVI '
        WriteCC("RIFF"u8);
        _riffSizePos = _file.Position; _bw.Write(0u);
        WriteCC("AVI "u8);

        // LIST 'hdrl'
        WriteCC("LIST"u8);
        _hdrlSizePos = _file.Position; _bw.Write(0u);
        WriteCC("hdrl"u8);
        var hdrlBodyStart = _file.Position;

        WriteAvih();
        WriteVideoStrl();
        WriteAudioStrl();

        _bw.Flush();
        // LIST size = 4 ('hdrl' text) + body content
        Patch32(_hdrlSizePos, (uint)(_file.Position - hdrlBodyStart + 4));

        // LIST 'movi'
        _moviListPos = _file.Position;
        WriteCC("LIST"u8);
        _moviSizePos = _file.Position; _bw.Write(0u);
        WriteCC("movi"u8);
    }

    private void WriteAvih()
    {
        WriteCC("avih"u8);
        _bw.Write(56u);
        _avihTotalFramesPos = _file.Position + 12; // offset to TotalFrames within AVIMAINHEADER

        _bw.Write(1_000_000u / (uint)_fps); // MicroSecPerFrame
        _bw.Write(0u);  // MaxBytesPerSec
        _bw.Write(0u);  // PaddingGranularity
        _bw.Write(0x110u); // Flags: AVIF_HASINDEX | AVIF_ISINTERLEAVED
        _bw.Write(0u);  // TotalFrames (patched later)
        _bw.Write(0u);  // InitialFrames
        _bw.Write(2u);  // Streams
        _bw.Write(0u);  // SuggestedBufferSize
        _bw.Write((uint)_width);
        _bw.Write((uint)_height);
        _bw.Write(0u); _bw.Write(0u); _bw.Write(0u); _bw.Write(0u); // Reserved
    }

    private void WriteVideoStrl()
    {
        WriteCC("LIST"u8);
        var sizePos = _file.Position; _bw.Write(0u);
        WriteCC("strl"u8);
        var bodyStart = _file.Position;

        // strh
        WriteCC("strh"u8);
        _bw.Write(56u);
        WriteCC("vids"u8);  // fccType
        WriteCC("MJPG"u8);  // fccHandler
        _bw.Write(0u);      // Flags
        _bw.Write((ushort)0); _bw.Write((ushort)0); // Priority, Language
        _bw.Write(0u);      // InitialFrames
        _bw.Write(1u);      // Scale
        _bw.Write((uint)_fps); // Rate
        _bw.Write(0u);      // Start
        _bw.Write(0u);      // Length (not critical here)
        _bw.Write(0u);      // SuggestedBufferSize
        _bw.Write(0xFFFFFFFFu); // Quality
        _bw.Write(0u);      // SampleSize
        _bw.Write((short)0); _bw.Write((short)0);
        _bw.Write((short)_width); _bw.Write((short)_height); // rcFrame

        // strf = BITMAPINFOHEADER
        WriteCC("strf"u8);
        _bw.Write(40u);
        _bw.Write(40u);         // biSize
        _bw.Write(_width);      // biWidth
        _bw.Write(_height);     // biHeight (positive = bottom-up)
        _bw.Write((ushort)1);   // biPlanes
        _bw.Write((ushort)24);  // biBitCount
        _bw.Write(0x47504A4Du); // biCompression = 'MJPG'
        _bw.Write(0u);          // biSizeImage
        _bw.Write(0); _bw.Write(0); // biXPelsPerMeter, biYPelsPerMeter
        _bw.Write(0u); _bw.Write(0u); // biClrUsed, biClrImportant

        _bw.Flush();
        Patch32(sizePos, (uint)(_file.Position - bodyStart + 4));
    }

    private void WriteAudioStrl()
    {
        var blockAlign = _channels * 2; // 16-bit PCM

        WriteCC("LIST"u8);
        var sizePos = _file.Position; _bw.Write(0u);
        WriteCC("strl"u8);
        var bodyStart = _file.Position;

        // strh
        WriteCC("strh"u8);
        _bw.Write(56u);
        WriteCC("auds"u8);  // fccType
        _bw.Write(0u);      // fccHandler = 0 for PCM
        _bw.Write(0u);      // Flags
        _bw.Write((ushort)0); _bw.Write((ushort)0);
        _bw.Write(0u);      // InitialFrames
        _bw.Write(1u);      // Scale
        _bw.Write((uint)_sampleRate); // Rate
        _bw.Write(0u);      // Start
        _bw.Write(0u);      // Length
        _bw.Write(0u);      // SuggestedBufferSize
        _bw.Write(0xFFFFFFFFu); // Quality
        _bw.Write((uint)blockAlign); // SampleSize
        _bw.Write((short)0); _bw.Write((short)0);
        _bw.Write((short)0); _bw.Write((short)0); // rcFrame

        // strf = WAVEFORMATEX
        WriteCC("strf"u8);
        _bw.Write(18u);
        _bw.Write((ushort)1);           // wFormatTag = PCM
        _bw.Write((ushort)_channels);
        _bw.Write((uint)_sampleRate);
        _bw.Write((uint)(_sampleRate * blockAlign)); // nAvgBytesPerSec
        _bw.Write((ushort)blockAlign);
        _bw.Write((ushort)16);          // wBitsPerSample
        _bw.Write((ushort)0);           // cbSize

        _bw.Flush();
        Patch32(sizePos, (uint)(_file.Position - bodyStart + 4));
    }

    private void WriteIdx1()
    {
        WriteCC("idx1"u8);
        var sizePos = _file.Position; _bw.Write(0u);
        var start = _file.Position;

        foreach (var (filePos, isVideo, dataSize) in _index)
        {
            WriteCC(isVideo ? "00dc"u8 : "01wb"u8);
            _bw.Write(0x10u); // AVIIF_KEYFRAME
            // dwOffset = offset of chunk from movi 'LIST' position + 4
            _bw.Write((uint)(filePos - _moviListPos - 4));
            _bw.Write((uint)dataSize);
        }

        _bw.Flush();
        Patch32(sizePos, (uint)(_file.Position - start));
    }

    private void WriteChunk(ReadOnlySpan<byte> id, byte[] data)
    {
        WriteCC(id);
        _bw.Write(data.Length);
        _bw.Write(data);
        if (data.Length % 2 != 0)
            _bw.Write((byte)0); // RIFF word alignment
    }

    private void WriteCC(ReadOnlySpan<byte> cc)
    {
        _bw.Write(cc);
    }

    private void Patch32(long position, uint value)
    {
        var current = _file.Position;
        _bw.Flush();
        _file.Seek(position, SeekOrigin.Begin);
        var bytes = new byte[4];
        bytes[0] = (byte)(value & 0xFF);
        bytes[1] = (byte)((value >> 8) & 0xFF);
        bytes[2] = (byte)((value >> 16) & 0xFF);
        bytes[3] = (byte)((value >> 24) & 0xFF);
        _file.Write(bytes, 0, 4);
        _file.Seek(current, SeekOrigin.Begin);
    }
}
