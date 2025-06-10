using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class MediaKeyHook : IDisposable
    {
        private const int WM_APPCOMMAND = 0x0319;
        private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
        private const int APPCOMMAND_MEDIA_STOP = 13;
        private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
        private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;

        public event EventHandler PlayPausePressed;
        public event EventHandler StopPressed;
        public event EventHandler NextTrackPressed;
        public event EventHandler PreviousTrackPressed;

        private readonly Form _form;

        public MediaKeyHook(Form form)
        {
            _form = form;
            _form.KeyPreview = true;
            _form.KeyDown += OnKeyDown;
        }

        public bool ProcessMessage(ref Message m)
        {
            if (m.Msg != WM_APPCOMMAND) return false;
            int command = (int)(((long)m.LParam >> 16) & 0xFFF);
            switch (command)
            {
                case APPCOMMAND_MEDIA_PLAY_PAUSE: PlayPausePressed?.Invoke(this, EventArgs.Empty); return true;
                case APPCOMMAND_MEDIA_STOP: StopPressed?.Invoke(this, EventArgs.Empty); return true;
                case APPCOMMAND_MEDIA_NEXTTRACK: NextTrackPressed?.Invoke(this, EventArgs.Empty); return true;
                case APPCOMMAND_MEDIA_PREVIOUSTRACK: PreviousTrackPressed?.Invoke(this, EventArgs.Empty); return true;
            }
            return false;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Space:
                    PlayPausePressed?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
                case Keys.Right when e.Control:
                    NextTrackPressed?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
                case Keys.Left when e.Control:
                    PreviousTrackPressed?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
            }
        }

        public void Dispose()
        {
            if (_form != null)
                _form.KeyDown -= OnKeyDown;
        }
    }
}
