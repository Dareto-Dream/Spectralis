using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Spectralis.Core.Platform;

namespace Spectralis.App.Views;

public enum CdnWarningChoice { Dismissed, ClosedApp, OpenedLink }

public sealed class CdnWarningViewModel
{
    private readonly CdnWarning _warning;

    public CdnWarningViewModel(CdnWarning warning, Application app)
    {
        _warning = warning;
        SeverityBrush = ResolveSeverityBrush(warning.Severity, app);
    }

    public string Title       => _warning.Title;
    public string Message     => _warning.Message;
    public bool   Dismissible => _warning.Dismissible;
    public bool   HasLink     => _warning.HasLink;
    public string LinkLabel   => string.IsNullOrWhiteSpace(_warning.LinkLabel) ? "Learn More" : _warning.LinkLabel;
    public IBrush SeverityBrush { get; }

    private static IBrush ResolveSeverityBrush(string severity, Application app)
    {
        var key = severity.ToLowerInvariant() switch
        {
            "critical" => "Brush.Signal",
            "info"     => "Brush.Ink.Secondary",
            _          => "Brush.Signal.Dim",   // "warning" and anything else
        };
        return app.TryFindResource(key, out var res) && res is IBrush b
            ? b
            : Brushes.Gray;
    }
}

public partial class CdnWarningWindow : Window
{
    private readonly CdnWarning _warning;
    public CdnWarningChoice Choice { get; private set; } = CdnWarningChoice.Dismissed;

    public CdnWarningWindow(CdnWarning warning)
    {
        _warning = warning;
        InitializeComponent();
        DataContext = new CdnWarningViewModel(warning, Application.Current!);

        // Non-dismissible warnings must not be closable via the title-bar X.
        if (!warning.Dismissible)
            Closing += (_, e) => { if (Choice == CdnWarningChoice.Dismissed) e.Cancel = true; };
    }

    public static async Task<CdnWarningChoice> ShowAsync(Window owner, CdnWarning warning)
    {
        var win = new CdnWarningWindow(warning);
        await win.ShowDialog(owner);
        return win.Choice;
    }

    private void OnOpenLink(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_warning.LinkUrl))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_warning.LinkUrl)
            {
                UseShellExecute = true,
            });
        }
        Choice = CdnWarningChoice.OpenedLink;
        // Dismissible: close dialog. Non-dismissible: link opened, keep app running (user can re-click).
        if (_warning.Dismissible) Close();
    }

    private void OnDismiss(object? sender, RoutedEventArgs e)
    {
        Choice = CdnWarningChoice.Dismissed;
        Close();
    }

    private void OnCloseApp(object? sender, RoutedEventArgs e)
    {
        Choice = CdnWarningChoice.ClosedApp;
        Close();
    }

    private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Escape) return;
        if (_warning.Dismissible) { Choice = CdnWarningChoice.Dismissed; Close(); }
    }
}
