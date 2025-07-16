using System;
using System.Collections.Generic;

namespace Spectralis.Core.Streaming
{
    public class StreamingRegistry : IDisposable
    {
        private readonly Dictionary<string, IStreamingSource> _sources =
            new Dictionary<string, IStreamingSource>(StringComparer.OrdinalIgnoreCase);

        public void Register(IStreamingSource source) => _sources[source.Name] = source;

        public IStreamingSource? TryGet(string name) =>
            _sources.TryGetValue(name, out var src) ? src : null;

        public IEnumerable<string> Names => _sources.Keys;

        public IEnumerable<IStreamingSource> Sources => _sources.Values;

        public void Dispose()
        {
            foreach (var src in _sources.Values)
                src.Dispose();
            _sources.Clear();
        }
    }
}
