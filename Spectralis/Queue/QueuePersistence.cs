using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Spectralis.Library;

namespace Spectralis.Queue
{
    public class QueuePersistence
    {
        private readonly string _path;

        public QueuePersistence(string path)
        {
            _path = path;
        }

        public void Save(PlayQueue queue)
        {
            var data = new QueueSnapshot
            {
                CurrentIndex = queue.CurrentIndex,
                RepeatMode = queue.RepeatMode,
                IsShuffled = queue.IsShuffled,
                Items = new List<TrackInfo>()
            };

            foreach (var item in queue.Items)
                data.Items.Add(item.Track);

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(_path, json);
        }

        public void Load(PlayQueue queue)
        {
            if (!File.Exists(_path)) return;

            try
            {
                string json = File.ReadAllText(_path);
                var data = JsonConvert.DeserializeObject<QueueSnapshot>(json);
                if (data == null) return;

                queue.Clear();
                queue.RepeatMode = data.RepeatMode;

                foreach (var track in data.Items ?? new List<TrackInfo>())
                    queue.Add(new PlayQueueItem(track));

                if (data.IsShuffled)
                    queue.SetShuffle(true);

                if (data.CurrentIndex >= 0 && data.CurrentIndex < queue.Count)
                    queue.PlayAt(data.CurrentIndex);
            }
            catch { }
        }

        private class QueueSnapshot
        {
            public int CurrentIndex { get; set; }
            public RepeatMode RepeatMode { get; set; }
            public bool IsShuffled { get; set; }
            public List<TrackInfo> Items { get; set; }
        }
    }
}
