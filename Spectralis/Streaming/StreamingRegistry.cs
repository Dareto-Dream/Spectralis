using System;
using System.Collections.Generic;

namespace Spectralis.Streaming
{
    public class StreamingRegistry
    {
        private readonly Dictionary<string, IStreamingSource> _sources =
            new Dictionary<string, IStreamingSource>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, IStreamingSource> Sources => _sources;

        public void Register(IStreamingSource source)
        {
            _sources[source.Name] = source;
        }

        public bool TryGet(string name, out IStreamingSource source) =>
            _sources.TryGetValue(name, out source);

        public IReadOnlyList<string> GetNames()
        {
            var list = new List<string>(_sources.Keys);
            list.Sort();
            return list;
        }
    }
}
