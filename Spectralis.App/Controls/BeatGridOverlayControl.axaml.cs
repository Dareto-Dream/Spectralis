using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Spectralis.Core.Analysis;

namespace Spectralis.App.Controls
{
    public partial class BeatGridOverlayControl : UserControl
    {
        public static readonly StyledProperty<BeatGrid?> BeatGridProperty =
            AvaloniaProperty.Register<BeatGridOverlayControl, BeatGrid?>(nameof(BeatGrid));

        public static readonly StyledProperty<double> TrackDurationProperty =
            AvaloniaProperty.Register<BeatGridOverlayControl, double>(nameof(TrackDuration), 1.0);

        public BeatGrid? BeatGrid
        {
            get => GetValue(BeatGridProperty);
            set => SetValue(BeatGridProperty, value);
        }

        public double TrackDuration
        {
            get => GetValue(TrackDurationProperty);
            set => SetValue(TrackDurationProperty, value);
        }

        private static readonly IPen DownbeatPen = new Pen(Brushes.White, 1.5);
        private static readonly IPen BeatPen = new Pen(new SolidColorBrush(Color.FromArgb(100, 200, 200, 255)), 0.8);

        public BeatGridOverlayControl()
        {
            InitializeComponent();
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            var grid = BeatGrid;
            if (grid == null || !grid.IsValid || TrackDuration < 0.1) return;

            double w = Bounds.Width;
            double h = Bounds.Height;

            foreach (var beat in grid.Beats)
            {
                double x = beat.TimeSeconds / TrackDuration * w;
                var pen = beat.IsDownbeat ? DownbeatPen : BeatPen;
                ctx.DrawLine(pen, new Point(x, 0), new Point(x, h));
            }
        }

        static BeatGridOverlayControl()
        {
            BeatGridProperty.Changed.AddClassHandler<BeatGridOverlayControl>((s, _) => s.InvalidateVisual());
            TrackDurationProperty.Changed.AddClassHandler<BeatGridOverlayControl>((s, _) => s.InvalidateVisual());
        }
    }
}
