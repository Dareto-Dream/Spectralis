using System;
using System.Text;

namespace Spectralis.Core.Lyrics
{
    public class LrcExporter
    {
        public string Export(LrcFile file)
        {
            var sb = new StringBuilder();

            foreach (var kv in file.Metadata)
                sb.AppendLine($"[{kv.Key}:{kv.Value}]");

            if (file.Metadata.Count > 0) sb.AppendLine();

            foreach (var line in file.Lines)
            {
                sb.Append(FormatTimestamp(line.Timestamp));
                if (line.IsEnhanced && line.Words != null)
                {
                    foreach (var word in line.Words)
                        sb.Append(FormatEnhancedTimestamp(word.Timestamp)).Append(word.Text);
                }
                else
                {
                    sb.Append(line.Text);
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string FormatTimestamp(TimeSpan ts) =>
            $"[{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}]";

        private static string FormatEnhancedTimestamp(TimeSpan ts) =>
            $"<{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}>";
    }
}
