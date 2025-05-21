using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Spectralis.Streaming
{
    public static class YtDlpValidator
    {
        public static bool IsValidPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!File.Exists(path)) return false;
            string name = Path.GetFileNameWithoutExtension(path).ToLower();
            return name == "yt-dlp" || name == "yt-dlp_x86" || name == "youtube-dl";
        }

        public static async Task<(bool ok, string version)> ProbeAsync(string path)
        {
            if (!IsValidPath(path)) return (false, null);

            try
            {
                var psi = new ProcessStartInfo(path, "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string output = await proc.StandardOutput.ReadToEndAsync();
                    await Task.Run(() => proc.WaitForExit(5000));
                    if (proc.ExitCode == 0)
                        return (true, output.Trim());
                }
            }
            catch { }

            return (false, null);
        }

        public static string FindInPath()
        {
            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathVar.Split(';'))
            {
                foreach (string name in new[] { "yt-dlp.exe", "yt-dlp" })
                {
                    string full = Path.Combine(dir.Trim(), name);
                    if (File.Exists(full)) return full;
                }
            }
            return null;
        }
    }
}
