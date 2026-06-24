using System.Drawing;
using Jint;

namespace Spectralis;

internal sealed class JsVisualizerRuntime
{
    private readonly ScriptCanvasContext _ctx = new();
    private Engine? _engine;
    private string _script;
    private string? _lastError;

    public JsVisualizerRuntime(string script)
    {
        _script = script;
    }

    public string? LastError => _lastError;

    public void SetScript(string script)
    {
        _script = script;
        _engine = null;
    }

    public void Draw(Graphics g, Rectangle bounds, VisualizerScene scene)
    {
        _engine ??= BuildEngine();
        _ctx.Begin(g);

        try
        {
            _engine.SetValue("scene", new
            {
                spectrum = Array.ConvertAll(scene.SpectrumLevels, f => (double)f),
                waveform = Array.ConvertAll(scene.WaveformPoints, f => (double)f),
                peak     = (double)scene.PeakLevel,
                rms      = (double)scene.RmsLevel,
                time     = (double)scene.PlaybackTimeSeconds,
                width    = bounds.Width,
                height   = bounds.Height,
            });

            _engine.Execute(_script);
            _lastError = null;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            try
            {
                using var font = new Font("Segoe UI", 9f);
                using var brush = new SolidBrush(Color.FromArgb(200, 255, 100, 100));
                g.DrawString($"Script error: {_lastError}", font, brush, 8f, 8f);
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
