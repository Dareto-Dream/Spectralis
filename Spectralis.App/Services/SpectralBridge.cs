using System;
using System.Text.Json;

namespace Spectralis.App.Services
{
    public class SpectralBridge
    {
        private readonly AlbumWorldService _world;
        private Func<float[]>? _spectrumGetter;
        private Func<TimeSpan>? _positionGetter;
        private Func<TimeSpan>? _durationGetter;

        public SpectralBridge(AlbumWorldService world)
        {
            _world = world;
        }

        public void SetAudioGetters(
            Func<float[]> spectrum,
            Func<TimeSpan> position,
            Func<TimeSpan> duration)
        {
            _spectrumGetter = spectrum;
            _positionGetter = position;
            _durationGetter = duration;
        }

        public string HandleMessage(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                string cmd = doc.RootElement.GetProperty("cmd").GetString() ?? string.Empty;

                return cmd switch
                {
                    "spectral.getFrame" => GetFrame(),
                    "spectral.meta" => GetMeta(),
                    "spectral.store.get" => HandleStoreGet(doc),
                    "spectral.store.set" => HandleStoreSet(doc),
                    _ => Error($"unknown command: {cmd}")
                };
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        private string GetFrame()
        {
            float[] bands = _spectrumGetter?.Invoke() ?? Array.Empty<float>();
            double pos = _positionGetter?.Invoke().TotalSeconds ?? 0;
            double dur = _durationGetter?.Invoke().TotalSeconds ?? 0;

            return JsonSerializer.Serialize(new
            {
                ok = true,
                bands,
                position = pos,
                duration = dur,
                progress = dur > 0 ? pos / dur : 0
            });
        }

        private string GetMeta()
        {
            var m = _world.Manifest;
            return JsonSerializer.Serialize(new
            {
                ok = true,
                albumTitle = m?.AlbumTitle,
                artist = m?.Artist,
                trackCount = m?.Tracks.Count ?? 0
            });
        }

        private string HandleStoreGet(JsonDocument doc)
        {
            string key = doc.RootElement.GetProperty("key").GetString() ?? string.Empty;
            var session = _world.Session;
            bool hasKey = session?.Stats.ContainsKey(key) == true;
            return JsonSerializer.Serialize(new { ok = true, exists = hasKey });
        }

        private string HandleStoreSet(JsonDocument doc)
        {
            return JsonSerializer.Serialize(new { ok = true });
        }

        private static string Error(string msg) =>
            JsonSerializer.Serialize(new { ok = false, error = msg });
    }
}
