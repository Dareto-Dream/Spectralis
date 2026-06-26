using Jint;

namespace Spectralis.Core.Visualizers.Scripting;

public sealed class JsVisualizerRuntime
{
    private readonly ScriptCanvasContext _ctx = new();
    private Engine? _engine;
    private string _script;

    public JsVisualizerRuntime(string script) => _script = script;

    public string? LastError { get; private set; }

    public void SetScript(string script)
    {
        _script = script;
        _engine = null;
    }

    public void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        _engine ??= BuildEngine();
        _ctx.Begin(canvas, bounds);

        try
        {
            _engine.SetValue("scene", new
            {
                spectrum = Array.ConvertAll(scene.SpectrumLevels, f => (double)f),
                waveform = Array.ConvertAll(scene.WaveformPoints, f => (double)f),
                peak = (double)scene.PeakLevel,
                rms = (double)scene.RmsLevel,
                time = (double)scene.PlaybackTimeSeconds,
                width = (double)bounds.Width,
                height = (double)bounds.Height,
            });

            _engine.Execute(_script);
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            try
            {
                // Draw error text so the script author can see what went wrong.
                canvas.DrawText($"Script error: {ex.Message}",
                    new VizRect(8, 8, bounds.Width - 16, 40),
                    new VizColor(200, 255, 100, 100), 11, VizTextAlign.Left);
            }
            catch { }
        }
    }

    private Engine BuildEngine()
    {
        var engine = new Engine(opts => opts.MaxStatements(50_000));
        engine.SetValue("ctx", _ctx);
        return engine;
    }
}
