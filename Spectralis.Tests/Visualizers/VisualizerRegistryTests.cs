using FluentAssertions;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Visualizers
{
    public class VisualizerRegistryTests
    {
        private static VisualizerRegistry BuildRegistry()
        {
            var r = new VisualizerRegistry();
            r.Register(new VisualizerInfo { Id = "test-viz", DisplayName = "Test", Category = "Test", Factory = () => new StubVisualizer() });
            r.Register(new VisualizerInfo { Id = "test-viz-2", DisplayName = "Test 2", Category = "Spectrum", Factory = () => new StubVisualizer() });
            return r;
        }

        [Fact]
        public void TryCreate_KnownId_ReturnsVisualizer()
        {
            var registry = BuildRegistry();
            var ok = registry.TryCreate("test-viz", out var viz);
            ok.Should().BeTrue();
            viz.Should().NotBeNull();
            viz!.Id.Should().Be("test-viz");
            viz.Dispose();
        }

        [Fact]
        public void TryCreate_UnknownId_ReturnsFalse()
        {
            var registry = BuildRegistry();
            var ok = registry.TryCreate("nonexistent", out var viz);
            ok.Should().BeFalse();
            viz.Should().BeNull();
        }

        [Fact]
        public void TryCreate_CreatesNewInstanceEachTime()
        {
            var registry = BuildRegistry();
            registry.TryCreate("test-viz", out var a);
            registry.TryCreate("test-viz", out var b);
            a.Should().NotBeSameAs(b);
            a!.Dispose();
            b!.Dispose();
        }

        [Fact]
        public void GetByCategory_ReturnsMatchingEntries()
        {
            var registry = BuildRegistry();
            var spectrum = registry.GetByCategory("Spectrum");
            spectrum.Should().HaveCount(1);
            spectrum[0].Id.Should().Be("test-viz-2");
        }

        [Fact]
        public void GetByCategory_NoMatch_ReturnsEmpty()
        {
            var registry = BuildRegistry();
            var result = registry.GetByCategory("Beat");
            result.Should().BeEmpty();
        }

        [Fact]
        public void Register_DuplicateId_OverwritesPrevious()
        {
            var registry = BuildRegistry();
            registry.Register(new VisualizerInfo { Id = "test-viz", DisplayName = "Overwritten", Category = "Test", Factory = () => new StubVisualizer() });
            registry.TryCreate("test-viz", out var viz);
            viz!.DisplayName.Should().Be("Overwritten");
            viz.Dispose();
        }
    }

    internal sealed class StubVisualizer : IVisualizer
    {
        public string Id => "test-viz";
        public string DisplayName { get; set; } = "Test";
        public string Category => "Test";
        public bool IsHardwareAccelerated => false;
        public void OnFrameReady(in Spectralis.Core.Audio.AudioFrame frame) { }
        public void OnSizeChanged(double width, double height) { }
        public void Render(IVisualizerRenderContext ctx) { }
        public void Dispose() { }
    }
}
