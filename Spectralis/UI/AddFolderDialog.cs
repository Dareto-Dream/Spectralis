using System;
using System.Drawing;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class AddFolderDialog : Form
    {
        private readonly ListBox _folderList;
        private readonly Button _btnAdd;
        private readonly Button _btnRemove;
        private readonly Button _btnOk;
        private readonly Button _btnCancel;

        public string[] SelectedFolders { get; private set; }

        public AddFolderDialog(string[] existingFolders)
        {
            Text = "Library Folders";
            Size = new Size(520, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var lblHint = new Label { Text = "Music library folders:", Location = new Point(12, 12), AutoSize = true };
            _folderList = new ListBox { Location = new Point(12, 34), Size = new Size(380, 270), BackColor = Color.FromArgb(26, 26, 32), ForeColor = Color.White };
            foreach (var f in existingFolders) _folderList.Items.Add(f);

            _btnAdd = new Button { Text = "Add Folder...", Location = new Point(400, 34), Size = new Size(100, 30) };
            _btnRemove = new Button { Text = "Remove", Location = new Point(400, 72), Size = new Size(100, 30) };
            _btnOk = new Button { Text = "OK", Location = new Point(320, 290), Size = new Size(90, 30), DialogResult = DialogResult.OK };
            _btnCancel = new Button { Text = "Cancel", Location = new Point(418, 290), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };

            _btnAdd.Click += OnAddFolder;
            _btnRemove.Click += OnRemoveFolder;
            _btnOk.Click += OnOk;

            Controls.AddRange(new Control[] { lblHint, _folderList, _btnAdd, _btnRemove, _btnOk, _btnCancel });
            AcceptButton = _btnOk; CancelButton = _btnCancel;
        }

        private void OnAddFolder(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog { Description = "Select music folder" };
            if (dlg.ShowDialog() == DialogResult.OK && !_folderList.Items.Contains(dlg.SelectedPath))
                _folderList.Items.Add(dlg.SelectedPath);
        }

        private void OnRemoveFolder(object sender, EventArgs e)
        {
            if (_folderList.SelectedIndex >= 0)
                _folderList.Items.RemoveAt(_folderList.SelectedIndex);
        }

        private void OnOk(object sender, EventArgs e)
        {
            SelectedFolders = new string[_folderList.Items.Count];
            for (int i = 0; i < _folderList.Items.Count; i++)
                SelectedFolders[i] = _folderList.Items[i].ToString();
        }
    }
}
