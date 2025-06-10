using System;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class LibraryPanelToggle : IDisposable
    {
        private readonly Form _owner;
        private readonly Panel _libraryPanel;
        private bool _visible;

        public LibraryPanelToggle(Form owner, Panel libraryPanel)
        {
            _owner = owner;
            _libraryPanel = libraryPanel;
            _libraryPanel.Visible = false;

            _owner.KeyDown += OnKeyDown;
            _owner.KeyPreview = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.L)
            {
                Toggle();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        public void Toggle()
        {
            _visible = !_visible;
            _libraryPanel.Visible = _visible;
            _owner.PerformLayout();
        }

        public bool IsVisible => _visible;

        public void Dispose()
        {
            _owner.KeyDown -= OnKeyDown;
        }
    }
}
