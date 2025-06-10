using System.Drawing;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public static class DarkTheme
    {
        public static readonly Color Background = Color.FromArgb(18, 18, 22);
        public static readonly Color Surface = Color.FromArgb(26, 26, 32);
        public static readonly Color Elevated = Color.FromArgb(35, 35, 44);
        public static readonly Color Accent = Color.FromArgb(100, 150, 255);
        public static readonly Color TextPrimary = Color.White;
        public static readonly Color TextSecondary = Color.FromArgb(180, 180, 190);
        public static readonly Color TextMuted = Color.FromArgb(100, 100, 110);
        public static readonly Color Border = Color.FromArgb(50, 50, 60);

        public static void Apply(Form form)
        {
            form.BackColor = Background;
            form.ForeColor = TextPrimary;
            ApplyRecursive(form);
        }

        private static void ApplyRecursive(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                switch (c)
                {
                    case Button b:
                        b.FlatStyle = FlatStyle.Flat;
                        b.BackColor = Elevated;
                        b.ForeColor = TextPrimary;
                        b.FlatAppearance.BorderColor = Border;
                        break;
                    case Panel p when !(p is AlbumArtPanel):
                        p.BackColor = Surface;
                        break;
                    case Label l:
                        l.ForeColor = TextPrimary;
                        l.BackColor = Color.Transparent;
                        break;
                    case MenuStrip ms:
                        ms.BackColor = Surface;
                        ms.ForeColor = TextPrimary;
                        break;
                    case StatusStrip ss:
                        ss.BackColor = Surface;
                        ss.ForeColor = TextSecondary;
                        break;
                }
                ApplyRecursive(c);
            }
        }
    }
}
