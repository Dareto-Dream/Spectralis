using System;
using System.Drawing;
using System.Windows.Forms;

namespace Spectralis
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            openToolStripMenuItem.Click += OnOpenFile;
            btnPlay.Click += OnPlay;
            btnPause.Click += OnPause;
            btnStop.Click += OnStop;
            btnPrev.Click += OnPrev;
            btnNext.Click += OnNext;
            progressBar.Scroll += OnProgressScroll;
            volumeBar.Scroll += OnVolumeScroll;
        }

        private void OnOpenFile(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Audio Files|*.mp3;*.flac;*.wav;*.ogg;*.opus;*.m4a;*.aac;*.mid|All Files|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
            }
        }

        private void OnPlay(object sender, EventArgs e) { }
        private void OnPause(object sender, EventArgs e) { }
        private void OnStop(object sender, EventArgs e) { }
        private void OnPrev(object sender, EventArgs e) { }
        private void OnNext(object sender, EventArgs e) { }
        private void OnProgressScroll(object sender, EventArgs e) { }
        private void OnVolumeScroll(object sender, EventArgs e) { }
    }
}
