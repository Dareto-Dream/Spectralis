using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class AboutDialog : Form
    {
        public AboutDialog()
        {
            Text = "About Spectralis";
            Size = new Size(340, 200);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var lblName = new Label { Text = "Spectralis", Font = new Font("Segoe UI", 16f, FontStyle.Bold), Location = new Point(20, 20), AutoSize = true };
            var lblVer = new Label { Text = $"Version {version.Major}.{version.Minor}.{version.Build}", Location = new Point(20, 55), AutoSize = true };
            var lblDesc = new Label { Text = "A music player with visualizers.", Location = new Point(20, 78), AutoSize = true };
            var btnOk = new Button { Text = "OK", Location = new Point(240, 140), Size = new Size(75, 28), DialogResult = DialogResult.OK };

            Controls.AddRange(new Control[] { lblName, lblVer, lblDesc, btnOk });
            AcceptButton = btnOk;
        }
    }
}
