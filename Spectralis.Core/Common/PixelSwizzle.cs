using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Spectralis.Core.Common;

/// <summary>
/// Red/blue channel swap for 4-byte pixels (RGBA to BGRA and back). The GPU renderers hand back RGBA while
/// the canvas wants BGRA, and that swap runs on every frame over the whole image, so it's vectorised: 16
/// bytes (4 pixels) per step, 32 with AVX2, with a scalar loop only for the tail. Bytes that don't make up a
/// whole pixel at the end are left untouched.
/// </summary>
public static class PixelSwizzle
{
    // Within each group of four bytes, swap positions 0 and 2: [2,1,0,3].
    private static readonly Vector128<byte> Mask128 =
        Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);

    public static void SwapRedBlueInPlace(Span<byte> pixels)
    {
        var i = 0;
        ref var start = ref MemoryMarshal.GetReference(pixels);

        if (Avx2.IsSupported)
        {
            var mask256 = Vector256.Create(Mask128, Mask128);
            for (; i <= pixels.Length - 32; i += 32)
            {
                var v = Vector256.LoadUnsafe(ref start, (nuint)i);
                Avx2.Shuffle(v, mask256).StoreUnsafe(ref start, (nuint)i);
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            for (; i <= pixels.Length - 16; i += 16)
            {
                var v = Vector128.LoadUnsafe(ref start, (nuint)i);
                Vector128.Shuffle(v, Mask128).StoreUnsafe(ref start, (nuint)i);
            }
        }

        for (; i + 3 < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }
    }

    /// <summary>Copies <paramref name="source"/> into <paramref name="destination"/> with red and blue swapped in one pass.</summary>
    public static void CopySwapRedBlue(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (destination.Length < source.Length)
        {
            throw new ArgumentException("Destination is smaller than the source.", nameof(destination));
        }

        source.CopyTo(destination);
        SwapRedBlueInPlace(destination[..source.Length]);
    }

    /// <summary>The plain loop the vector code must always agree with. Exposed so tests and benchmarks have one reference.</summary>
    public static void SwapRedBlueScalar(Span<byte> pixels)
    {
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }
    }
}
