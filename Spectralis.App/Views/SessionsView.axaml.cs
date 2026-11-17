using Avalonia.Controls;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class SessionsView : UserControl
{
    private SessionsViewModel? subscribed;
    private Window? competitionWindow;
    public SessionsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (subscribed is not null)
            {
                subscribed.Listening.CopyToClipboardRequested -= Copy;
                subscribed.Requests.CopyToClipboardRequested -= Copy;
                subscribed.Requests.OpenUrlRequested -= OpenUrl;
            }
            subscribed = DataContext as SessionsViewModel;
            if (subscribed is not null)
            {
                subscribed.Listening.CopyToClipboardRequested += Copy;
                subscribed.Requests.CopyToClipboardRequested += Copy;
                subscribed.Requests.OpenUrlRequested += OpenUrl;
            }
        };
        SizeChanged += (_, _) =>
        {
            var stacked = Bounds.Width < 860;
            Workbench.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "3*,32,2*");
            Grid.SetColumn(RoundPanel, stacked ? 0 : 2);
            Grid.SetRow(RoundPanel, stacked ? 1 : 0);
            RoundPanel.Margin = new Avalonia.Thickness(0, stacked ? 28 : 0, 0, 0);
        };
        CompetitionView.PopOutRequested = () =>
        {
            if (DataContext is not SessionsViewModel vm) return;
            if (competitionWindow is not null) { competitionWindow.Activate(); return; }
            var window = new PageWindow("Song Wars") { Width = 650, Height = 800 };
            window.PageHost.Content = new ScrollViewer { Content = new SongWarsView { DataContext = vm.Competition, Margin = new Avalonia.Thickness(24) } };
            competitionWindow = window;
            window.Closed += (_, _) => competitionWindow = null;
            if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner);
            else window.Show();
        };
    }
    private async void Copy(string text)
    {
        try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text); }
        catch (Exception) { }
    }
    private void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }
}
