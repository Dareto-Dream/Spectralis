using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectralis.Queue
{
    public static class QueueShuffle
    {
        public static List<int> BuildOrder(int count, int pinFirst = -1, Random rng = null)
        {
            rng = rng ?? new Random();
            var order = Enumerable.Range(0, count).ToList();

            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = order[i];
                order[i] = order[j];
                order[j] = tmp;
            }

            if (pinFirst >= 0 && pinFirst < count)
            {
                int pos = order.IndexOf(pinFirst);
                if (pos > 0)
                {
                    order.RemoveAt(pos);
                    order.Insert(0, pinFirst);
                }
            }

            return order;
        }

        public static List<int> Reshuffle(List<int> current, int count, int skipFirst = -1, Random rng = null)
        {
            var next = BuildOrder(count, -1, rng);
            if (skipFirst >= 0 && next.Count > 0 && next[0] == skipFirst)
            {
                if (next.Count > 1)
                {
                    int swap = (rng ?? new Random()).Next(1, next.Count);
                    int tmp = next[0];
                    next[0] = next[swap];
                    next[swap] = tmp;
                }
            }
            return next;
        }
    }
}
