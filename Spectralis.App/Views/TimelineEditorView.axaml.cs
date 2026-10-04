using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Spectralis.App.Controls;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class TimelineEditorView : UserControl
{
    private DispatcherTimer? _positionTimer;

    public TimelineEditorView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => StartTimer();
        DetachedFromVisualTree += (_, _) => StopTimer();

        // "Fit" needs to know how wide the scroll area is.
        CanvasScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty && DataContext is TimelineEditorViewModel vm)
                vm.ViewportWidth = CanvasScroll.Viewport.Width;
        };
    }

    private void StartTimer()
    {
        _positionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) =>
        {
            if (DataContext is not TimelineEditorViewModel vm) return;
            vm.TickPosition();
            KeepPlayheadInView(vm);
        });
        _positionTimer.Start();
    }

    private void StopTimer()
    {
        _positionTimer?.Stop();
        _positionTimer = null;
    }

    // While the track plays, scroll along with the playhead instead of letting it run off the edge.
    private void KeepPlayheadInView(TimelineEditorViewModel vm)
    {
        if (!vm.HasTrack || !vm.IsPlaying) return;

        var x = vm.Layout.TimeToX(vm.PositionSeconds);
        var viewport = CanvasScroll.Viewport.Width;
        var left = CanvasScroll.Offset.X;
        if (viewport <= 0 || (x >= left + TimelineLayout.GutterWidth && x <= left + viewport - 24)) return;

        CanvasScroll.Offset = new Vector(Math.Max(0, x - (viewport * 0.25)), CanvasScroll.Offset.Y);
    }
}
