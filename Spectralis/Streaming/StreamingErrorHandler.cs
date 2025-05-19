using System;
using System.Net.Http;

namespace Spectralis.Streaming
{
    public enum StreamingError
    {
        Unknown,
        NotAuthenticated,
        NetworkFailure,
        RateLimited,
        NotFound,
        Forbidden,
        ServiceUnavailable,
        YtDlpNotFound,
        YtDlpFailed
    }

    public static class StreamingErrorHandler
    {
        public static StreamingError Classify(Exception ex)
        {
            if (ex is InvalidOperationException ioe)
            {
                if (ioe.Message.Contains("Not authenticated")) return StreamingError.NotAuthenticated;
                if (ioe.Message.Contains("No audio stream")) return StreamingError.YtDlpFailed;
                if (ioe.Message.Contains("yt-dlp")) return StreamingError.YtDlpNotFound;
            }

            if (ex is HttpRequestException hre)
            {
                if (hre.Message.Contains("401")) return StreamingError.NotAuthenticated;
                if (hre.Message.Contains("403")) return StreamingError.Forbidden;
                if (hre.Message.Contains("404")) return StreamingError.NotFound;
                if (hre.Message.Contains("429")) return StreamingError.RateLimited;
                if (hre.Message.Contains("503")) return StreamingError.ServiceUnavailable;
                return StreamingError.NetworkFailure;
            }

            return StreamingError.Unknown;
        }

        public static string Describe(StreamingError error, string source) => error switch
        {
            StreamingError.NotAuthenticated => $"{source}: not signed in. Open Streaming → Setup.",
            StreamingError.RateLimited => $"{source} rate limit hit — wait a moment.",
            StreamingError.NotFound => "Track not available.",
            StreamingError.Forbidden => $"{source}: access denied.",
            StreamingError.YtDlpNotFound => "yt-dlp not found. Set path in Streaming → YouTube.",
            StreamingError.YtDlpFailed => "yt-dlp failed to extract audio.",
            StreamingError.NetworkFailure => "Network error. Check your connection.",
            StreamingError.ServiceUnavailable => $"{source} is currently unavailable.",
            _ => "An unexpected error occurred."
        };
    }
}
