using System;
using System.Linq;

namespace Spectralis.Queue
{
    public static class QueueStats
    {
        public static TimeSpan TotalDuration(PlayQueue queue)
        {
            double totalSecs = queue.Items.Sum(i => i.Track?.Duration.TotalSeconds ?? 0);
            return TimeSpan.FromSeconds(totalSecs);
        }

        public static TimeSpan RemainingDuration(PlayQueue queue)
        {
            int current = queue.CurrentIndex;
            if (current < 0) return TotalDuration(queue);

            double secs = 0;
            var items = queue.Items;
            for (int i = current; i < items.Count; i++)
                secs += items[i].Track?.Duration.TotalSeconds ?? 0;

            return TimeSpan.FromSeconds(secs);
        }

        public static string Summary(PlayQueue queue)
        {
            int count = queue.Count;
            TimeSpan total = TotalDuration(queue);

            string timeStr = total.TotalHours >= 1
                ? $"{(int)total.TotalHours}h {total.Minutes}m"
                : $"{total.Minutes}m {total.Seconds}s";

            return $"{count} track{(count == 1 ? "" : "s")} — {timeStr}";
        }
    }
}
