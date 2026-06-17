using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace Spectralis.App.Services
{
    public class HotkeyBinding
    {
        public Key Key { get; set; }
        public KeyModifiers Modifiers { get; set; }
        public Action Action { get; set; } = () => { };
        public string Description { get; set; } = string.Empty;
    }

    public class HotkeyService : IDisposable
    {
        private readonly List<HotkeyBinding> _bindings = new();
        private bool _disposed;

        public void Register(Key key, KeyModifiers modifiers, Action action, string description = "")
        {
            _bindings.Add(new HotkeyBinding
            {
                Key = key,
                Modifiers = modifiers,
                Action = action,
                Description = description
            });
        }

        public bool HandleKeyDown(Key key, KeyModifiers modifiers)
        {
            foreach (var b in _bindings)
            {
                if (b.Key == key && b.Modifiers == modifiers)
                {
                    b.Action();
                    return true;
                }
            }
            return false;
        }

        public IReadOnlyList<HotkeyBinding> Bindings => _bindings;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bindings.Clear();
        }
    }
}
