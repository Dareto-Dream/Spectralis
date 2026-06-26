using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spectralis.App.Views;

public enum UpdatePromptChoice { RemindLater, UpdateNow, DontRemindAgain }

public partial class UpdatePromptWindow : Window
{
    public UpdatePromptChoice Choice { get; private set; } = UpdatePromptChoice.RemindLater;

    public UpdatePromptWindow(string? updateVersion)
    {
        InitializeComponent();
        var versionText = string.IsNullOrWhiteSpace(updateVersion)
            ? "A newer version of Spectralis is available."
            : $"Spectralis {updateVersion.Trim()} is available.";
        MessageLabel.Text = $"{versionText}\n\nWould you like to install it now?";
    }

    public static async Task<UpdatePromptChoice> ShowAsync(Window owner, string? updateVersion)
    {
        var win = new UpdatePromptWindow(updateVersion);
        await win.ShowDialog(owner);
        return win.Choice;
    }

    private void OnDontRemind(object? sender, RoutedEventArgs e) => CloseWith(UpdatePromptChoice.DontRemindAgain);
    private void OnRemindLater(object? sender, RoutedEventArgs e) => CloseWith(UpdatePromptChoice.RemindLater);
    private void OnUpdateNow(object? sender, RoutedEventArgs e) => CloseWith(UpdatePromptChoice.UpdateNow);

    private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Escape) CloseWith(UpdatePromptChoice.RemindLater);
    }

    private void CloseWith(UpdatePromptChoice choice) { Choice = choice; Close(); }
}
