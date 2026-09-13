using Spectralis.App.Worlds;
using Xunit;

namespace Spectralis.Tests.App;

/// <summary>
/// Exercises <see cref="WgpuWorldRenderer"/> — the managed P/Invoke wrapper — against the real
/// native wgpu-host library on whatever GPU this machine has, same spirit as wgpu-host's own
/// Rust smoke tests (tests/smoke.rs) but proving the .NET marshaling boundary specifically.
/// Uses <see cref="WgpuWorldRenderer.RenderFrameBgraPixels"/> rather than <c>RenderFrame</c> —
/// the latter constructs an Avalonia <c>WriteableBitmap</c>, which needs full platform/app
/// init this plain xUnit host doesn't have (confirmed: throws
/// "Unable to locate 'Avalonia.Platform.IPlatformRenderInterface'" otherwise). Skips (doesn't
/// fail) when the native library or a compatible GPU isn't available, since both are legitimate
/// environment limitations (e.g. a CI box with no GPU), not code defects.
/// </summary>
public sealed class WgpuWorldRendererTests
{
    [Fact]
    public void RenderFrameBgraPixels_ProducesTheRightSizedBufferWithAVisibleCube()
    {
        using var renderer = WgpuWorldRenderer.Create(64, 48);
        if (renderer is null)
        {
            return; // no native lib / no GPU adapter on this machine — not a code defect
        }

        var pixels = renderer.RenderFrameBgraPixels(0.6, 0.4f, 0.3f, 3.0f);

        Assert.NotNull(pixels);
        Assert.Equal(64 * 48 * 4, pixels!.Length);

        // Mirrors wgpu-host's own Rust smoke test: the clear color is a fixed dark
        // blue-gray, so if the cube actually rendered, plenty of pixels must differ from
        // it and from each other (BGRA order here, vs RGBA on the Rust side).
        var clear = new byte[] { (byte)(0.08f * 255), (byte)(0.05f * 255), (byte)(0.05f * 255) };
        var nonClearPixels = 0;
        var distinctColors = new HashSet<(byte, byte, byte)>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var bgr = (pixels[i], pixels[i + 1], pixels[i + 2]);
            if (bgr != (clear[0], clear[1], clear[2]))
            {
                nonClearPixels++;
            }
            distinctColors.Add(bgr);
            Assert.Equal(255, pixels[i + 3]); // alpha fully opaque
        }

        Assert.True(nonClearPixels > 100, $"expected a visible cube, got {nonClearPixels} non-background pixels");
        Assert.True(distinctColors.Count > 3, $"expected multiple distinct colors, got {distinctColors.Count}");
    }

    [Fact]
    public void RenderFrameBgraPixels_TwiceInARow_ReusesTheSameArrayInstance()
    {
        using var renderer = WgpuWorldRenderer.Create(32, 32);
        if (renderer is null)
        {
            return;
        }

        var first = renderer.RenderFrameBgraPixels(0.0, 0.0f, 0.0f, 3.0f);
        var second = renderer.RenderFrameBgraPixels(1.0, 0.5f, 0.2f, 3.0f);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Dispose_ThenRenderFrameBgraPixels_ReturnsNullInsteadOfCrashing()
    {
        var renderer = WgpuWorldRenderer.Create(16, 16);
        if (renderer is null)
        {
            return;
        }

        renderer.Dispose();

        Assert.Null(renderer.RenderFrameBgraPixels(0.0, 0.0f, 0.0f, 3.0f));
    }

    [Fact]
    public void Create_ZeroDimensions_ClampsToOne()
    {
        using var renderer = WgpuWorldRenderer.Create(0, 0);
        if (renderer is null)
        {
            return;
        }

        Assert.Equal(1, renderer.Width);
        Assert.Equal(1, renderer.Height);
    }
}
