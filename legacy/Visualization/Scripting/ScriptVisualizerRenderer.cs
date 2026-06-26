using System.Drawing;

namespace Spectralis;

internal sealed class ScriptVisualizerRenderer : IVisualizerRenderer
{
    private readonly JsVisualizerRuntime _runtime;

    public ScriptVisualizerRenderer(ScriptedVisualizerDefinition def)
    {
        _runtime = new JsVisualizerRuntime(def.Script);
    }

    public string? LastError => _runtime.LastError;

    public void UpdateScript(string script) => _runtime.SetScript(script);

    public void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene) =>
        _runtime.Draw(graphics, bounds, scene);
}
