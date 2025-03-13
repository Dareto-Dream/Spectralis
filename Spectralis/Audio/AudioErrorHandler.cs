using System;
using System.IO;

namespace Spectralis.Audio
{
    public enum AudioError
    {
        FileNotFound,
        UnsupportedFormat,
        CorruptFile,
        DeviceError,
        Unknown
    }

    public static class AudioErrorHandler
    {
        public static AudioError Classify(Exception ex, string filePath)
        {
            if (!File.Exists(filePath)) return AudioError.FileNotFound;
            if (ex is NotSupportedException) return AudioError.UnsupportedFormat;
            if (ex is InvalidDataException || ex is FormatException) return AudioError.CorruptFile;
            if (ex is System.Runtime.InteropServices.COMException) return AudioError.DeviceError;
            return AudioError.Unknown;
        }

        public static string Describe(AudioError error, string filePath)
        {
            switch (error)
            {
                case AudioError.FileNotFound: return $"File not found: {Path.GetFileName(filePath)}";
                case AudioError.UnsupportedFormat: return $"Unsupported format: {Path.GetExtension(filePath)}";
                case AudioError.CorruptFile: return $"File appears corrupt: {Path.GetFileName(filePath)}";
                case AudioError.DeviceError: return "Audio device error — check output device settings";
                default: return "An unknown playback error occurred";
            }
        }
    }
}
