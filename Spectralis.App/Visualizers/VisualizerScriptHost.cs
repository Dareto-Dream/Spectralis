using System;
using System.Collections.Generic;
using Jint;
using Jint.Native;
using Spectralis.Core.Audio;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Visualizers
{
    public sealed class VisualizerScriptHost : SkiaVisualizerBase
    {
        private readonly Engine _engine;
        private readonly string _scriptSource;
        private bool _initialized;

        public override string Id => "script:" + _scriptId;
        public override string DisplayName { get; }
        public override string Category => "Script";

        private readonly string _scriptId;

        public VisualizerScriptHost(string scriptId, string displayName, string scriptSource)
        {
            _scriptId = scriptId;
            DisplayName = displayName;
            _scriptSource = scriptSource;
            _engine = new Engine(opts =>
            {
                opts.LimitRecursion(64);
                opts.MaxStatements(50_000);
                opts.TimeoutInterval(TimeSpan.FromMilliseconds(16));
            });
        }

        private void EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;

            var api = new VisualizerScriptApi();
            _engine.SetValue("spectral", api);
            _engine.Execute(_scriptSource);
        }

        protected override void RenderSkia(SkiaSharp.SKCanvas canvas, double width, double height)
        {
            try
            {
                EnsureInit();
                _engine.SetValue("_w", width);
                _engine.SetValue("_h", height);
                _engine.SetValue("_spectrum", Spectrum);
                _engine.SetValue("_waveform", Waveform);
                _engine.SetValue("_rms", (RmsLeft + RmsRight) / 2f);
                _engine.Invoke("render");
            }
            catch (Exception)
            {
                // script errors are swallowed to avoid crashing the render thread
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _engine.Dispose();
            base.Dispose(disposing);
        }
    }
}
