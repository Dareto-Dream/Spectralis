using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Audio;
using Spectralis.Queue;

namespace Spectralis.UI
{
    public class TransportControls : Panel
    {
        private readonly AudioEngine _engine;
        private readonly PlayQueue _queue;

        private readonly Button _btnPrev;
        private readonly Button _btnPlay;
        private readonly Button _btnPause;
        private readonly Button _btnStop;
        private readonly Button _btnNext;
        private readonly TrackBar _tbarVolume;

        public TransportControls(AudioEngine engine, PlayQueue queue)
        {
            _engine = engine;
            _queue = queue;

            Height = 44;
            BackColor = Color.FromArgb(18, 18, 26);

            _btnPrev = MakeBtn("◀◀", 36);
            _btnPlay = MakeBtn("▶", 44);
            _btnPause = MakeBtn("⏸", 44);
            _btnStop = MakeBtn("■", 36);
            _btnNext = MakeBtn("▶▶", 36);

            _tbarVolume = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 80,
                Width = 100,
                Height = 26,
                TickStyle = TickStyle.None,
                TickFrequency = 0
            };

            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(6, 4, 6, 4)
            };

            layout.Controls.Add(_btnPrev);
            layout.Controls.Add(_btnPlay);
            layout.Controls.Add(_btnPause);
            layout.Controls.Add(_btnStop);
            layout.Controls.Add(_btnNext);
            layout.Controls.Add(new Label { Width = 12, Height = 1, AutoSize = false });
            layout.Controls.Add(new Label { Text = "Vol", Width = 26, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(120, 120, 130) });
            layout.Controls.Add(_tbarVolume);

            Controls.Add(layout);

            _btnPrev.Click += (s, e) =>
            {
                var item = _queue.Previous();
                if (item != null) { _engine.Load(item.Track.FilePath); _engine.Play(); }
            };

            _btnPlay.Click += (s, e) => _engine.Play();
            _btnPause.Click += (s, e) => _engine.Pause();
            _btnStop.Click += (s, e) => _engine.Stop();

            _btnNext.Click += (s, e) =>
            {
                var item = _queue.Next();
                if (item != null) { _engine.Load(item.Track.FilePath); _engine.Play(); }
            };

            _tbarVolume.Scroll += (s, e) => _engine.Volume = _tbarVolume.Value / 100f;
        }

        private static Button MakeBtn(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 42),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f)
            };
        }
    }
}
