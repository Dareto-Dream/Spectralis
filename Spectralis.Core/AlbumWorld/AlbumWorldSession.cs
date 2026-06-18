using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.AlbumWorld
{
    public class AlbumWorldSession
    {
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        public string WorldId { get; set; } = string.Empty;
        public Dictionary<string, WorldPlayStats> Stats { get; set; } = new();
        public HashSet<string> EarnedAchievements { get; set; } = new();
        public DateTimeOffset LastOpened { get; set; } = DateTimeOffset.UtcNow;

        public void RecordPlay(string trackId, TimeSpan duration)
        {
            if (!Stats.TryGetValue(trackId, out var stat))
            {
                stat = new WorldPlayStats { TrackId = trackId };
                Stats[trackId] = stat;
            }
            stat.PlayCount++;
            stat.TotalListenTime += duration;
            stat.LastPlayed = DateTimeOffset.UtcNow;
        }

        public void UnlockTrack(string trackId)
        {
            if (Stats.TryGetValue(trackId, out var stat))
                stat.Unlocked = true;
            else
                Stats[trackId] = new WorldPlayStats { TrackId = trackId, Unlocked = true };
        }

        public void EarnAchievement(string achievementId) =>
            EarnedAchievements.Add(achievementId);

        public bool HasPlayed(string trackId) =>
            Stats.TryGetValue(trackId, out var s) && s.PlayCount > 0;

        public static async Task<AlbumWorldSession?> LoadAsync(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                string json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<AlbumWorldSession>(json, _json);
            }
            catch { return null; }
        }

        public async Task SaveAsync(string path)
        {
            LastOpened = DateTimeOffset.UtcNow;
            string tmp = path + ".tmp";
            string json = JsonSerializer.Serialize(this, _json);
            await File.WriteAllTextAsync(tmp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
