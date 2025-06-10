using System;
using System.Configuration;

namespace Spectralis
{
    public static class AppSettings
    {
        public static int DefaultVolume
        {
            get => int.TryParse(ConfigurationManager.AppSettings["DefaultVolume"], out var v) ? v : 80;
        }

        public static string LastOpenDirectory
        {
            get => ConfigurationManager.AppSettings["LastOpenDirectory"] ?? string.Empty;
            set => UpdateSetting("LastOpenDirectory", value);
        }

        public static string VisualizerMode
        {
            get => ConfigurationManager.AppSettings["VisualizerMode"] ?? "Spectrum";
            set => UpdateSetting("VisualizerMode", value);
        }

        public static string LibraryPath
        {
            get => ConfigurationManager.AppSettings["LibraryPath"] ?? string.Empty;
            set => UpdateSetting("LibraryPath", value);
        }

        private static void UpdateSetting(string key, string value)
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            if (config.AppSettings.Settings[key] != null)
                config.AppSettings.Settings[key].Value = value;
            else
                config.AppSettings.Settings.Add(key, value);
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }
}
