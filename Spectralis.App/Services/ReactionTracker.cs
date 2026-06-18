using System;
using System.Collections.Generic;
using Spectralis.Core.SharedPlay;

namespace Spectralis.App.Services
{
    public class ReactionTracker
    {
        private readonly List<(SharedPlayReactionMessage Reaction, DateTimeOffset Received)> _history = new();
        private const int MaxHistory = 200;

        public event EventHandler<SharedPlayReactionMessage>? ReactionReceived;

        public void Record(SharedPlayReactionMessage reaction)
        {
            if (_history.Count >= MaxHistory)
                _history.RemoveAt(0);
            _history.Add((reaction, DateTimeOffset.UtcNow));
            ReactionReceived?.Invoke(this, reaction);
        }

        public IReadOnlyList<(SharedPlayReactionMessage, DateTimeOffset)> GetHistory() => _history;

        public Dictionary<string, int> GetEmojiCounts()
        {
            var counts = new Dictionary<string, int>();
            foreach (var (r, _) in _history)
            {
                if (!counts.ContainsKey(r.Emoji)) counts[r.Emoji] = 0;
                counts[r.Emoji]++;
            }
            return counts;
        }

        public void Clear() => _history.Clear();
    }
}
