using System;
using System.Windows.Forms;
using Spectralis.Queue;

namespace Spectralis.Audio
{
    public class MediaKeySupport : IMessageFilter, IDisposable
    {
        private const int WM_APPCOMMAND = 0x0319;
        private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
        private const int APPCOMMAND_MEDIA_STOP = 13;
        private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
        private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;

        private readonly AudioEngine _engine;
        private readonly PlayQueue _queue;
        private bool _disposed;

        public MediaKeySupport(AudioEngine engine, PlayQueue queue)
        {
            _engine = engine;
            _queue = queue;
            Application.AddMessageFilter(this);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_APPCOMMAND) return false;

            int cmd = (int)(m.LParam.ToInt64() >> 16) & 0xFFF;

            switch (cmd)
            {
                case APPCOMMAND_MEDIA_PLAY_PAUSE:
                    if (_engine.IsPlaying) _engine.Pause();
                    else _engine.Play();
                    return true;

                case APPCOMMAND_MEDIA_STOP:
                    _engine.Stop();
                    return true;

                case APPCOMMAND_MEDIA_NEXTTRACK:
                    var next = _queue.Next();
                    if (next != null) { _engine.Load(next.Track.FilePath); _engine.Play(); }
                    return true;

                case APPCOMMAND_MEDIA_PREVIOUSTRACK:
                    var prev = _queue.Previous();
                    if (prev != null) { _engine.Load(prev.Track.FilePath); _engine.Play(); }
                    return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Application.RemoveMessageFilter(this);
        }
    }
}
