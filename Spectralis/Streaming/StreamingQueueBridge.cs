using System.Collections.Generic;
using Spectralis.Library;

namespace Spectralis.Streaming
{
    public static class StreamingQueueBridge
    {
        public static List<TrackInfo> ToQueue(IEnumerable<StreamingTrack> tracks)
        {
            var result = new List<TrackInfo>();
            foreach (var t in tracks)
                result.Add(StreamingMetadataBridge.ToTrackInfo(t));
            return result;
        }

        public static void EnqueueAll(IEnumerable<StreamingTrack> tracks, IList<TrackInfo> queue)
        {
            foreach (var t in tracks)
                queue.Add(StreamingMetadataBridge.ToTrackInfo(t));
        }
    }
}
