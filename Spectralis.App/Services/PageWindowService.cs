using Avalonia.Controls;
using Avalonia.Layout;
using Spectralis.App.Views;

namespace Spectralis.App.Services;

/// <summary>One control and one model per page, hosted in exactly one window.
/// Returning a page never constructs a second view or resets its model.</summary>
public sealed class PageWindowService(ContentControl dock, object shell, Func<object, Control> createView) : IDisposable
{
    private readonly Dictionary<object, PageWindow> _windows = new(ReferenceEqualityComparer.Instance);
    private object? _current;
    private string _title = "";
    private bool _closing;
    public event Action<PageWindow>? WindowOpened;
    public void Select(object page, string title)
    {
        _current = page; _title = title; ShowSelected();
    }
    public PageWindow? Find(object page) => _windows.GetValueOrDefault(page);
    private Control View(object page) { var view = createView(page); view.DataContext = page; return view; }
    private void ShowSelected()
    {
        if (!_closing && _current is not null && !_windows.ContainsKey(_current) && ReferenceEquals(dock.Content, View(_current))) return;
        dock.Content = null;
        if (_closing || _current is null) return;
        if (_windows.TryGetValue(_current, out var window))
        {
            var focus = new Button { Content = "Show window" }; focus.Click += (_, _) => Focus(window);
            var restore = new Button { Content = "Return to app" }; restore.Click += (_, _) => window.Close();
            dock.Content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Spacing = 16, Children = { new TextBlock { Text = _title + " is open in another window", FontSize = 20 }, focus, restore } };
        }
        else dock.Content = View(_current);
    }
    public PageWindow? PopOutCurrent()
    {
        if (_current is null || _closing) return null;
        if (_windows.TryGetValue(_current, out var existing)) { Focus(existing); return existing; }
        var page = _current; var view = View(page); dock.Content = null;
        var window = new PageWindow(_title) { DataContext = shell };
        _windows.Add(page, window); window.PageHost.Content = view;
        window.Closed += (_, _) =>
        {
            window.PageHost.Content = null; _windows.Remove(page);
            window.DataContext = null;
            if (!_closing && ReferenceEquals(page, _current)) ShowSelected();
        };
        WindowOpened?.Invoke(window); window.Show(); ShowSelected(); return window;
    }
    public void Return(object page) { if (_windows.TryGetValue(page, out var window)) window.Close(); }
    public static void Focus(Window window) { if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); }
    public void Dispose()
    {
        _closing = true;
        foreach (var window in _windows.Values.ToArray()) window.Close();
        _windows.Clear(); dock.Content = null;
    }
}
