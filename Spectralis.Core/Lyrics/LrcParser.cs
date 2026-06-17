using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Spectralis.Core.Lyrics
{
    public class LrcParser
    {
        private static readonly Regex TimestampRx = new(@"\[(\d{1,3}):(\d{2})\.(\d{2,3})\]", RegexOptions.Compiled);
        private static readonly Regex MetadataRx = new(@"\[(\w+):(.*)\]", RegexOptions.Compiled);
        private static readonly Regex EnhancedWordRx = new(@"<(\d{2}):(\d{2})\.(\d{2,3})>([^<]*)", RegexOptions.Compiled);

        public LrcFile Parse(string content)
        {
            var file = new LrcFile();
            if (string.IsNullOrEmpty(content)) return file;

            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r').Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("//") || line.StartsWith("#")) continue;

                var metaMatch = MetadataRx.Match(line);
                if (metaMatch.Success && !TimestampRx.IsMatch(line))
                {
                    file.Metadata[metaMatch.Groups[1].Value.ToLowerInvariant()] =
                        metaMatch.Groups[2].Value.Trim();
                    continue;
                }

                var timestamps = new List<TimeSpan>();
                var remaining = TimestampRx.Replace(line, m =>
                {
                    int min = int.Parse(m.Groups[1].Value);
                    int sec = int.Parse(m.Groups[2].Value);
                    string msStr = m.Groups[3].Value;
                    int ms = msStr.Length == 2 ? int.Parse(msStr) * 10 : int.Parse(msStr);
                    timestamps.Add(new TimeSpan(0, 0, min, sec, ms));
                    return string.Empty;
                });

                if (timestamps.Count == 0) continue;
                remaining = remaining.Trim();

                var enhancedWords = new List<EnhancedWord>();
                if (remaining.Contains('<'))
                {
                    foreach (Match wm in EnhancedWordRx.Matches(remaining))
                    {
                        int min = int.Parse(wm.Groups[1].Value);
                        int sec = int.Parse(wm.Groups[2].Value);
                        string msStr = wm.Groups[3].Value;
                        int ms = msStr.Length == 2 ? int.Parse(msStr) * 10 : int.Parse(msStr);
                        enhancedWords.Add(new EnhancedWord
                        {
                            Timestamp = new TimeSpan(0, 0, min, sec, ms),
                            Text = wm.Groups[4].Value
                        });
                    }
                }

                foreach (var ts in timestamps)
                {
                    file.Lines.Add(new LrcLine
                    {
                        Timestamp = ts,
                        Text = remaining,
                        Words = enhancedWords.Count > 0 ? enhancedWords : null
                    });
                }
            }

            file.Lines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return file;
        }

        public static TimeSpan ParseTimestamp(string raw)
        {
            var m = TimestampRx.Match(raw);
            if (!m.Success) return TimeSpan.Zero;
            int min = int.Parse(m.Groups[1].Value);
            int sec = int.Parse(m.Groups[2].Value);
            string msStr = m.Groups[3].Value;
            int ms = msStr.Length == 2 ? int.Parse(msStr) * 10 : int.Parse(msStr);
            return new TimeSpan(0, 0, min, sec, ms);
        }
    }
}
