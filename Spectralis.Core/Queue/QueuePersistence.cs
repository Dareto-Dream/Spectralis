using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Spectralis.Core.Models;

namespace Spectralis.Core.Queue
{
    public class QueueSnapshot
    {
        public int CurrentIndex { get; set; }
        public bool IsShuffled { get; set; }
        public RepeatMode RepeatMode { get; set; }
        public List<QueueSnapshotItem> Items { get; set; } = new();
    }

    public class QueueSnapshotItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    public class QueuePersistence
    {
        private readonly string _filePath;
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        public QueuePersistence(string filePath)
        {
            _filePath = filePath;
        }

        public async Task SaveAsync(PlayQueue queue)
        {
            var snapshot = new QueueSnapshot
            {
                CurrentIndex = queue.CurrentIndex,
                IsShuffled = queue.IsShuffled,
                RepeatMode = queue.RepeatMode
            };

            foreach (var item in queue.Items)
            {
                snapshot.Items.Add(new QueueSnapshotItem
                {
                    FilePath = item.Track.FilePath,
                    Title = item.Track.Title,
                    Artist = item.Track.Artist,
                    Duration = item.Track.Duration
                });
            }

            string json = JsonSerializer.Serialize(snapshot, _opts);
            string dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(_filePath, json);
        }

        public async Task<QueueSnapshot?> LoadAsync()
        {
            if (!File.Exists(_filePath)) return null;
            try
            {
                string json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<QueueSnapshot>(json);
            }
            catch
            {
                return null;
            }
        }

        public void RestoreInto(PlayQueue queue, QueueSnapshot snapshot)
        {
            queue.Clear();
            foreach (var item in snapshot.Items)
            {
                var track = new TrackInfo
                {
                    FilePath = item.FilePath,
                    Title = item.Title,
                    Artist = item.Artist,
                    Duration = item.Duration
                };
                queue.Enqueue(new PlayQueueItem(track));
            }

            queue.SetShuffle(snapshot.IsShuffled);
            queue.RepeatMode = snapshot.RepeatMode;
            if (snapshot.CurrentIndex >= 0 && snapshot.CurrentIndex < queue.Count)
                queue.PlayAt(snapshot.CurrentIndex);
        }
    }
}
