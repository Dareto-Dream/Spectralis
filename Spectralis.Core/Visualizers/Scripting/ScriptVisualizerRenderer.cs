namespace Spectralis.Core.Visualizers.Scripting;

public sealed class ScriptVisualizerRenderer : IVisualizerRenderer
{
    private readonly JsVisualizerRuntime _runtime;

    public ScriptVisualizerRenderer(ScriptedVisualizerDefinition def)
    {
        Definition = def;
        _runtime = new JsVisualizerRuntime(def.Script);
    }

    public ScriptedVisualizerDefinition Definition { get; }

    public string? LastError => _runtime.LastError;

    public void UpdateScript(string script) => _runtime.SetScript(script);

    public void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene) =>
        _runtime.Draw(canvas, bounds, scene);
}
