using Spectralis.App.Visualizers;
using Xunit;

namespace Spectralis.Tests.Visualizers
{
    public class Bar3DVisualizerTests
    {
        [Fact]
        public void Id_IsExpected()
        {
            var viz = new Bar3DVisualizer();
            Assert.Equal("bars-3d", viz.Id);
        }

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            var viz = new Bar3DVisualizer();
            var ex = Record.Exception(() => viz.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var viz = new Bar3DVisualizer();
            viz.Dispose();
            var ex = Record.Exception(() => viz.Dispose());
            Assert.Null(ex);
        }
    }
}
