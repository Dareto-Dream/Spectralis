using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Streaming;

namespace Spectralis.UI
{
    public class StreamingAuthDialog : Form
    {
        private readonly StreamingAuthStore _store;
        private TabControl _tabs;

        private TextBox _txtSpotifyClientId;
        private TextBox _txtSoundCloudClientId;
        private TextBox _txtSunoToken;
        private TextBox _txtYtDlpPath;

        public StreamingAuthDialog(StreamingAuthStore store)
        {
            _store = store;
            Text = "Streaming Services";
            Size = new Size(440, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            _tabs = new TabControl { Dock = DockStyle.Fill };

            _tabs.TabPages.Add(CreateSpotifyTab());
            _tabs.TabPages.Add(CreateSoundCloudTab());
            _tabs.TabPages.Add(CreateSunoTab());
            _tabs.TabPages.Add(CreateYouTubeTab());

            var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 30 };
            btnSave.Click += OnSave;

            Controls.Add(_tabs);
            Controls.Add(btnSave);
        }

        private TabPage CreateSpotifyTab()
        {
            var tab = new TabPage("Spotify");
            var lbl = new Label { Text = "Client ID:", Location = new Point(10, 15), AutoSize = true };
            _txtSpotifyClientId = new TextBox
            {
                Location = new Point(90, 12),
                Width = 300,
                Text = _store.GetSpotifyTokens()?.AccessToken != null ? "(configured)" : ""
            };
            tab.Controls.AddRange(new Control[] { lbl, _txtSpotifyClientId });
            var btnAuth = new Button { Text = "Connect", Location = new Point(90, 45), Width = 100 };
            tab.Controls.Add(btnAuth);
            return tab;
        }

        private TabPage CreateSoundCloudTab()
        {
            var tab = new TabPage("SoundCloud");
            var lbl = new Label { Text = "Client ID:", Location = new Point(10, 15), AutoSize = true };
            _txtSoundCloudClientId = new TextBox
            {
                Location = new Point(90, 12),
                Width = 300,
                Text = _store.GetSoundCloudClientId() ?? ""
            };
            tab.Controls.AddRange(new Control[] { lbl, _txtSoundCloudClientId });
            return tab;
        }

        private TabPage CreateSunoTab()
        {
            var tab = new TabPage("Suno");
            var lbl = new Label { Text = "Session token:", Location = new Point(10, 15), AutoSize = true };
            _txtSunoToken = new TextBox
            {
                Location = new Point(110, 12),
                Width = 270,
                PasswordChar = '•',
                Text = _store.GetSunoSessionToken() ?? ""
            };
            tab.Controls.AddRange(new Control[] { lbl, _txtSunoToken });
            return tab;
        }

        private TabPage CreateYouTubeTab()
        {
            var tab = new TabPage("YouTube");
            var lbl = new Label { Text = "yt-dlp path:", Location = new Point(10, 15), AutoSize = true };
            _txtYtDlpPath = new TextBox
            {
                Location = new Point(90, 12),
                Width = 250,
                Text = _store.GetYtDlpPath() ?? "yt-dlp"
            };
            tab.Controls.AddRange(new Control[] { lbl, _txtYtDlpPath });
            return tab;
        }

        private void OnSave(object sender, EventArgs e)
        {
            _store.SetSoundCloudClientId(_txtSoundCloudClientId.Text.Trim());
            _store.SetSunoSessionToken(_txtSunoToken.Text.Trim());
            _store.SetYtDlpPath(_txtYtDlpPath.Text.Trim());
        }
    }
}
