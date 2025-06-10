using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class VisualizerKeyboardShortcuts : IDisposable
    {
        private readonly Form _owner;
        private readonly VisualizerPanel _panel;
        private readonly IReadOnlyList<string> _names;
        private int _currentIndex;
        private Action<string> _onSwitch;
        private Action _onToggleOverlay;

        public VisualizerKeyboardShortcuts(
            Form owner,
            VisualizerPanel panel,
            IReadOnlyList<string> names,
            Action<string> onSwitch,
            Action onToggleOverlay)
        {
            _owner = owner;
            _panel = panel;
            _names = names;
            _onSwitch = onSwitch;
            _onToggleOverlay = onToggleOverlay;
            _owner.KeyDown += OnKeyDown;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Right)
            {
                _currentIndex = (_currentIndex + 1) % _names.Count;
                _onSwitch?.Invoke(_names[_currentIndex]);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Left)
            {
                _currentIndex = (_currentIndex - 1 + _names.Count) % _names.Count;
                _onSwitch?.Invoke(_names[_currentIndex]);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.D)
            {
                _onToggleOverlay?.Invoke();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        public void Dispose()
        {
            _owner.KeyDown -= OnKeyDown;
        }
    }
}
