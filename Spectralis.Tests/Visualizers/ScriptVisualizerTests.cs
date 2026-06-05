using System;
using Spectralis.App.Visualizers;
using Xunit;

namespace Spectralis.Tests.Visualizers
{
    public class ScriptVisualizerTests
    {
        [Fact]
        public void ScriptVisualizer_Id_IsExpected()
        {
            var viz = new ScriptVisualizer();
            Assert.Equal("script", viz.Id);
        }

        [Fact]
        public void ScriptVisualizer_DefaultScript_IsEmpty()
        {
            var viz = new ScriptVisualizer();
            Assert.Null(viz.Script);
        }

        [Fact]
        public void ScriptVisualizer_SetScript_PersistsValue()
        {
            var viz = new ScriptVisualizer();
            viz.Script = "function render(ctx) {}";
            Assert.Equal("function render(ctx) {}", viz.Script);
        }

        [Fact]
        public void ScriptVisualizer_Dispose_DoesNotThrow()
        {
            var viz = new ScriptVisualizer();
            var ex = Record.Exception(() => viz.Dispose());
            Assert.Null(ex);
        }
    }
}
