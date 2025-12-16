using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Visualizers
{
    public class VisualizerPresetStoreTests
    {
        private static string TempPath() =>
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        [Fact]
        public async Task LoadAsync_Missing_ReturnsEmpty()
        {
            var store = new VisualizerPresetStore(TempPath());
            var result = await store.LoadAsync();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task UpsertAsync_AddsNew()
        {
            string path = TempPath();
            var store = new VisualizerPresetStore(path);
            var preset = new VisualizerPreset { Name = "Test", VisualizerId = "spectrum-bars" };

            await store.UpsertAsync(preset);
            var list = await store.LoadAsync();
            list.Should().HaveCount(1);
            list[0].Name.Should().Be("Test");
        }

        [Fact]
        public async Task UpsertAsync_UpdatesExisting()
        {
            string path = TempPath();
            var store = new VisualizerPresetStore(path);
            var preset = new VisualizerPreset { Name = "Before", VisualizerId = "waveform" };
            await store.UpsertAsync(preset);

            preset.Name = "After";
            await store.UpsertAsync(preset);

            var list = await store.LoadAsync();
            list.Should().HaveCount(1);
            list[0].Name.Should().Be("After");
        }

        [Fact]
        public async Task DeleteAsync_RemovesById()
        {
            string path = TempPath();
            var store = new VisualizerPresetStore(path);
            var a = new VisualizerPreset { Name = "A", VisualizerId = "v" };
            var b = new VisualizerPreset { Name = "B", VisualizerId = "v" };
            await store.UpsertAsync(a);
            await store.UpsertAsync(b);

            await store.DeleteAsync(a.Id);
            var list = await store.LoadAsync();
            list.Should().HaveCount(1);
            list[0].Name.Should().Be("B");
        }

        [Fact]
        public async Task GetForVisualizerAsync_FiltersById()
        {
            string path = TempPath();
            var store = new VisualizerPresetStore(path);
            await store.UpsertAsync(new VisualizerPreset { Name = "S1", VisualizerId = "spectrum-bars" });
            await store.UpsertAsync(new VisualizerPreset { Name = "W1", VisualizerId = "waveform" });

            var spectrumPresets = await store.GetForVisualizerAsync("spectrum-bars");
            spectrumPresets.Should().HaveCount(1);
            spectrumPresets[0].Name.Should().Be("S1");
        }

        [Fact]
        public async Task DeleteAllAsync_RemovesAllForVisualizer()
        {
            string path = TempPath();
            var store = new VisualizerPresetStore(path);
            await store.UpsertAsync(new VisualizerPreset { Name = "S1", VisualizerId = "spiral" });
            await store.UpsertAsync(new VisualizerPreset { Name = "S2", VisualizerId = "spiral" });
            await store.UpsertAsync(new VisualizerPreset { Name = "W1", VisualizerId = "waveform" });

            await store.DeleteAllAsync("spiral");
            var list = await store.LoadAsync();
            list.Should().HaveCount(1);
            list[0].Name.Should().Be("W1");
        }
    }
}
