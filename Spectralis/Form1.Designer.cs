namespace Spectralis
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openToolStripMenuItem;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel;
        private System.Windows.Forms.TrackBar progressBar;
        private System.Windows.Forms.TrackBar volumeBar;
        private System.Windows.Forms.Button btnPlay;
        private System.Windows.Forms.Button btnPause;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnPrev;
        private System.Windows.Forms.Button btnNext;
        private System.Windows.Forms.Panel visualizerPanel;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblArtist;
        private System.Windows.Forms.Label lblTime;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.menuStrip = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.statusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.progressBar = new System.Windows.Forms.TrackBar();
            this.volumeBar = new System.Windows.Forms.TrackBar();
            this.btnPlay = new System.Windows.Forms.Button();
            this.btnPause = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnPrev = new System.Windows.Forms.Button();
            this.btnNext = new System.Windows.Forms.Button();
            this.visualizerPanel = new System.Windows.Forms.Panel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblArtist = new System.Windows.Forms.Label();
            this.lblTime = new System.Windows.Forms.Label();

            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { this.fileToolStripMenuItem });
            this.fileToolStripMenuItem.DropDownItems.Add(this.openToolStripMenuItem);
            this.fileToolStripMenuItem.Text = "&File";
            this.openToolStripMenuItem.Text = "&Open...";
            this.openToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O;

            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { this.statusLabel });
            this.statusLabel.Text = "Ready";

            this.progressBar.Location = new System.Drawing.Point(12, 680);
            this.progressBar.Size = new System.Drawing.Size(900, 45);
            this.progressBar.Maximum = 1000;
            this.progressBar.TickStyle = System.Windows.Forms.TickStyle.None;

            this.volumeBar.Location = new System.Drawing.Point(920, 680);
            this.volumeBar.Size = new System.Drawing.Size(90, 45);
            this.volumeBar.Maximum = 100;
            this.volumeBar.Value = 80;
            this.volumeBar.TickStyle = System.Windows.Forms.TickStyle.None;

            this.btnPrev.Location = new System.Drawing.Point(12, 630);
            this.btnPrev.Size = new System.Drawing.Size(75, 40);
            this.btnPrev.Text = "|<";

            this.btnPlay.Location = new System.Drawing.Point(95, 630);
            this.btnPlay.Size = new System.Drawing.Size(75, 40);
            this.btnPlay.Text = "Play";

            this.btnPause.Location = new System.Drawing.Point(178, 630);
            this.btnPause.Size = new System.Drawing.Size(75, 40);
            this.btnPause.Text = "Pause";

            this.btnStop.Location = new System.Drawing.Point(261, 630);
            this.btnStop.Size = new System.Drawing.Size(75, 40);
            this.btnStop.Text = "Stop";

            this.btnNext.Location = new System.Drawing.Point(344, 630);
            this.btnNext.Size = new System.Drawing.Size(75, 40);
            this.btnNext.Text = ">|";

            this.lblTitle.Location = new System.Drawing.Point(12, 580);
            this.lblTitle.Size = new System.Drawing.Size(600, 24);
            this.lblTitle.Text = "No track loaded";
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 12f, System.Drawing.FontStyle.Bold);

            this.lblArtist.Location = new System.Drawing.Point(12, 604);
            this.lblArtist.Size = new System.Drawing.Size(600, 20);
            this.lblArtist.Text = "";

            this.lblTime.Location = new System.Drawing.Point(850, 604);
            this.lblTime.Size = new System.Drawing.Size(160, 20);
            this.lblTime.Text = "0:00 / 0:00";
            this.lblTime.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.visualizerPanel.Location = new System.Drawing.Point(0, 24);
            this.visualizerPanel.Size = new System.Drawing.Size(1024, 550);
            this.visualizerPanel.BackColor = System.Drawing.Color.Black;

            this.SuspendLayout();
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1024, 768);
            this.Controls.Add(this.menuStrip);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.volumeBar);
            this.Controls.Add(this.btnPrev);
            this.Controls.Add(this.btnPlay);
            this.Controls.Add(this.btnPause);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.btnNext);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblArtist);
            this.Controls.Add(this.lblTime);
            this.Controls.Add(this.visualizerPanel);
            this.MainMenuStrip = this.menuStrip;
            this.MinimumSize = new System.Drawing.Size(1040, 807);
            this.Name = "Form1";
            this.Text = "Spectralis";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
