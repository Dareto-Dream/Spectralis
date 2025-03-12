using System;
using System.Drawing;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class PlaybackToolbar : Panel
    {
        public readonly Button BtnPrev;
        public readonly Button BtnPlay;
        public readonly Button BtnPause;
        public readonly Button BtnStop;
        public readonly Button BtnNext;
        public readonly Button BtnShuffle;
        public readonly Button BtnRepeat;

        public PlaybackToolbar()
        {
            Size = new Size(500, 44);
            BackColor = Color.FromArgb(25, 25, 30);

            BtnPrev    = MakeBtn("|<",  0);
            BtnPlay    = MakeBtn(">",   52);
            BtnPause   = MakeBtn("||",  104);
            BtnStop    = MakeBtn("[]",  156);
            BtnNext    = MakeBtn(">|",  208);
            BtnShuffle = MakeBtn("~",   280);
            BtnRepeat  = MakeBtn("R",   328);

            Controls.AddRange(new Control[] { BtnPrev, BtnPlay, BtnPause, BtnStop, BtnNext, BtnShuffle, BtnRepeat });
        }

        private static Button MakeBtn(string text, int x)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, 2),
                Size = new Size(44, 40),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(35, 35, 42),
                Font = new Font("Consolas", 10f)
            };
        }
    }
}
