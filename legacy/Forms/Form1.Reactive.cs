using System.IO;
using System.Text.Json;

namespace Spectralis;

public partial class Form1
{
    private readonly ReactiveRuntime reactiveRuntime = new();
    private string? reactiveLoadedPath;

    private void InitializeReactive()
    {
        reactiveRuntime.SectionChanged += (_, _) => { };
        reactiveRuntime.ParamsChanged += (_, _) => { };
    }

    private void LoadReactiveSidecar(string audioPath)
    {
        if (string.Equals(reactiveLoadedPath, audioPath, StringComparison.OrdinalIgnoreCase))
            return;

        reactiveLoadedPath = null;
        reactiveRuntime.Load(null);

        var sidecarPath = Path.ChangeExtension(audioPath, null) + ".spectralis-reactive.json";
        if (!File.Exists(sidecarPath))
            return;

        try
        {
            var json = File.ReadAllText(sidecarPath);
            var doc = JsonSerializer.Deserialize<ReactiveTimelineDocument>(json);
            reactiveRuntime.Load(doc);
            reactiveLoadedPath = audioPath;
        }
        catch { }
    }

    private void LoadReactiveDocument(ReactiveTimelineDocument? doc, string? sourcePath = null)
    {
        reactiveRuntime.Load(doc);
        reactiveLoadedPath = sourcePath;
    }

    private void AdvanceReactive()
    {
        if (reactiveLoadedPath is null)
            return;

        if (!engine.IsPlaying)
            return;

        reactiveRuntime.Advance(engine.GetPosition());
    }

    private void SeekReactive(double positionSeconds)
    {
        if (reactiveLoadedPath is null)
            return;

        reactiveRuntime.Seek(positionSeconds);
    }

    private void UnloadReactive()
    {
        reactiveRuntime.Load(null);
        reactiveLoadedPath = null;
    }
}
