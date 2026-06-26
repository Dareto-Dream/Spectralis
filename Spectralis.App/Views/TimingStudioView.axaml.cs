using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class TimingStudioView : UserControl
{
    private DispatcherTimer? _positionTimer;

    public TimingStudioView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => StartTimer();
        DetachedFromVisualTree += (_, _) => StopTimer();
    }

    private void StartTimer()
    {
        _positionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background,
            (_, _) => (DataContext as TimingStudioViewModel)?.TickPosition());
        _positionTimer.Start();

        if (DataContext is TimingStudioViewModel vm)
        {
            vm.ClipboardWriter = async text =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clip)
                    await clip.SetTextAsync(text);
            };
        }
    }

    private void StopTimer()
    {
        _positionTimer?.Stop();
        _positionTimer = null;
    }

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: TimedLineRow row } &&
            DataContext is TimingStudioViewModel vm)
        {
            var index = vm.Rows.IndexOf(row);
            if (index >= 0) vm.SelectRow(index);
        }
    }

    private void OnChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: TimingChip chip } &&
            DataContext is TimingStudioViewModel vm)
        {
            vm.SelectChip(chip.GlobalIndex);
        }
    }
}
