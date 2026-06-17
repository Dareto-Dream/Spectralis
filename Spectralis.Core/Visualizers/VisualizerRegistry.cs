using System;
using System.Collections.Generic;

namespace Spectralis.Core.Visualizers
{
    public class VisualizerRegistry
    {
        private readonly Dictionary<string, VisualizerInfo> _registered = new(StringComparer.OrdinalIgnoreCase);

        public void Register(VisualizerInfo info)
        {
            _registered[info.Id] = info;
        }

        public bool TryCreate(string id, out IVisualizer? visualizer)
        {
            if (_registered.TryGetValue(id, out var info))
            {
                visualizer = info.Factory();
                return true;
            }
            visualizer = null;
            return false;
        }

        public IReadOnlyDictionary<string, VisualizerInfo> All => _registered;

        public IEnumerable<VisualizerInfo> GetByCategory(string category)
        {
            foreach (var info in _registered.Values)
                if (string.Equals(info.Category, category, StringComparison.OrdinalIgnoreCase))
                    yield return info;
        }
    }
}
