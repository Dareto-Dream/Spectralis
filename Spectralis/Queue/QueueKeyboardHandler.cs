using System.Windows.Forms;
using Spectralis.Audio;

namespace Spectralis.Queue
{
    public class QueueKeyboardHandler
    {
        private readonly PlayQueue _queue;
        private readonly AudioEngine _engine;

        public QueueKeyboardHandler(PlayQueue queue, AudioEngine engine)
        {
            _queue = queue;
            _engine = engine;
        }

        public bool HandleKey(Keys keys)
        {
            bool ctrl = (keys & Keys.Control) != 0;
            Keys key = keys & Keys.KeyCode;

            if (ctrl && key == Keys.Right)
            {
                var next = _queue.Next();
                if (next != null) { _engine.Load(next.Track.FilePath); _engine.Play(); }
                return true;
            }

            if (ctrl && key == Keys.Left)
            {
                var prev = _queue.Previous();
                if (prev != null) { _engine.Load(prev.Track.FilePath); _engine.Play(); }
                return true;
            }

            if (ctrl && key == Keys.S)
            {
                _queue.SetShuffle(!_queue.IsShuffled);
                return true;
            }

            if (ctrl && key == Keys.R)
            {
                _queue.RepeatMode = _queue.RepeatMode switch
                {
                    RepeatMode.None => RepeatMode.RepeatAll,
                    RepeatMode.RepeatAll => RepeatMode.RepeatOne,
                    _ => RepeatMode.None
                };
                return true;
            }

            return false;
        }
    }
}
