using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class KeyboardShortcutHandler
    {
        private readonly Dictionary<Keys, Action> _shortcuts = new Dictionary<Keys, Action>();
        private readonly Form _form;

        public KeyboardShortcutHandler(Form form)
        {
            _form = form;
            _form.KeyPreview = true;
            _form.KeyDown += OnKeyDown;
        }

        public void Register(Keys key, Action action)
        {
            _shortcuts[key] = action;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_shortcuts.TryGetValue(e.KeyCode, out var action))
            {
                action?.Invoke();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        public void Unregister()
        {
            if (_form != null)
                _form.KeyDown -= OnKeyDown;
        }
    }
}
