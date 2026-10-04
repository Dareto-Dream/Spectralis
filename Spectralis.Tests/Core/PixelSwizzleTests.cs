using Spectralis.Core.Common;
using Xunit;

namespace Spectralis.Tests.Core;

public class PixelSwizzleTests
{
    private static byte[] Random(int length, int seed = 11)
    {
        var bytes = new byte[length];
        new System.Random(seed).NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public void One_pixel_swaps_red_and_blue_only()
    {
        byte[] px = [10, 20, 30, 40];

        PixelSwizzle.SwapRedBlueInPlace(px);

        Assert.Equal([30, 20, 10, 40], px);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(1000)]
    [InlineData(4 * 1280 * 720)]
    public void Vector_path_matches_the_scalar_reference_at_every_length(int length)
    {
        var expected = Random(length);
        var actual = (byte[])expected.Clone();

        PixelSwizzle.SwapRedBlueScalar(expected);
        PixelSwizzle.SwapRedBlueInPlace(actual);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Trailing_bytes_that_are_not_a_whole_pixel_are_left_alone()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7];

        PixelSwizzle.SwapRedBlueInPlace(data);

        Assert.Equal([3, 2, 1, 4, 5, 6, 7], data);
    }

    [Fact]
    public void Swapping_twice_restores_the_original()
    {
        var original = Random(4 * 5000, seed: 3);
        var work = (byte[])original.Clone();

        PixelSwizzle.SwapRedBlueInPlace(work);
        Assert.NotEqual(original, work);
        PixelSwizzle.SwapRedBlueInPlace(work);

        Assert.Equal(original, work);
    }

    [Fact]
    public void It_works_on_any_alignment_and_never_touches_bytes_outside_the_span()
    {
        var buffer = Random(200, seed: 5);
        var guardBefore = buffer[..13].ToArray();
        var guardAfter = buffer[^13..].ToArray();
        var expected = buffer.AsSpan(13, 174).ToArray();
        PixelSwizzle.SwapRedBlueScalar(expected);

        PixelSwizzle.SwapRedBlueInPlace(buffer.AsSpan(13, 174)); // deliberately misaligned slice

        Assert.Equal(expected, buffer.AsSpan(13, 174).ToArray());
        Assert.Equal(guardBefore, buffer[..13]);
        Assert.Equal(guardAfter, buffer[^13..]);
    }

    [Fact]
    public void Copy_swaps_into_the_destination_and_leaves_the_source_alone()
    {
        var source = Random(4 * 333, seed: 9);
        var sourceCopy = (byte[])source.Clone();
        var destination = new byte[source.Length];
        var expected = (byte[])source.Clone();
        PixelSwizzle.SwapRedBlueScalar(expected);

        PixelSwizzle.CopySwapRedBlue(source, destination);

        Assert.Equal(expected, destination);
        Assert.Equal(sourceCopy, source);
    }

    [Fact]
    public void Copy_into_a_larger_buffer_only_writes_the_source_length()
    {
        byte[] source = [1, 2, 3, 4];
        var destination = new byte[8];
        Array.Fill(destination, (byte)99);

        PixelSwizzle.CopySwapRedBlue(source, destination);

        Assert.Equal([3, 2, 1, 4, 99, 99, 99, 99], destination);
    }

    [Fact]
    public void Copy_into_a_too_small_buffer_throws()
    {
        Assert.Throws<ArgumentException>(() => PixelSwizzle.CopySwapRedBlue(new byte[8], new byte[4]));
    }
}
