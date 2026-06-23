using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.App.Services;

namespace Spectralis.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        DetailsText.Text = DiagnosticsSnapshot.Build();
    }

    private async void OnCopyVersion(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not null)
        {
            await Clipboard.SetTextAsync($"Spectralis {DiagnosticsSnapshot.CurrentVersion}");
        }
    }

    private void OnVisitWebsite(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://deltavdevs.com",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            SpectralisLog.Error("Failed to open DeltaVDevs website.", ex);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
