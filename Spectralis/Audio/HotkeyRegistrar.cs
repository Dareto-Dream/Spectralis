using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Spectralis.Audio
{
    public class HotkeyRegistrar : IMessageFilter, IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        private readonly IntPtr _hwnd;
        private readonly Dictionary<int, Action> _handlers = new Dictionary<int, Action>();
        private int _nextId = 0x1000;
        private bool _disposed;

        public HotkeyRegistrar(IntPtr hwnd)
        {
            _hwnd = hwnd;
            Application.AddMessageFilter(this);
        }

        public int Register(uint modifiers, uint vk, Action handler)
        {
            int id = _nextId++;
            if (RegisterHotKey(_hwnd, id, modifiers, vk))
                _handlers[id] = handler;
            return id;
        }

        public void Unregister(int id)
        {
            UnregisterHotKey(_hwnd, id);
            _handlers.Remove(id);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_HOTKEY) return false;
            int id = m.WParam.ToInt32();
            if (_handlers.TryGetValue(id, out var handler))
            {
                handler();
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (int id in _handlers.Keys)
                UnregisterHotKey(_hwnd, id);
            _handlers.Clear();
            Application.RemoveMessageFilter(this);
        }
    }
}
