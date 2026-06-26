using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal enum StreamAlign { Left, CenterH, Right, Top, MiddleV, Bottom }

/// <summary>
/// A drag-and-drop canvas that represents a 16:9 OBS stream.
/// Widgets correspond to ObsLayoutWidget instances.
/// </summary>
internal sealed class ObsEditorCanvas : Control
{
    private const int StreamW = 1920;
    private const int StreamH = 1080;
    private const int HandleSize = 9;
    private const double SnapStep = 1.0 / 40.0; // 2.5%

    private readonly ThemePalette palette;
    private List<ObsLayoutWidget> widgets = [];
    private int selectedIndex = -1;

    // drag state
    private bool isDragging;
    private bool isResizing;
    private ResizeHandle activeHandle;
    private PointF dragStart;
    private RectangleF dragOrigRect;

    public ObsEditorCanvas(ThemePalette palette)
    {
        this.palette = palette;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(14, 14, 18);
        Cursor = Cursors.Default;
        TabStop = true;
    }

    public bool SnapToGrid { get; set; }

    public event EventHandler? SelectionChanged;
    public event EventHandler? LayoutChanged;

    public IReadOnlyList<ObsLayoutWidget> Widgets => widgets;

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (selectedIndex == value) return;
            selectedIndex = value;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ObsLayoutWidget? SelectedWidget =>
        selectedIndex >= 0 && selectedIndex < widgets.Count ? widgets[selectedIndex] : null;

    public void SetWidgets(IEnumerable<ObsLayoutWidget> source)
    {
        widgets = source.Select(w => w.Clone()).ToList();
        selectedIndex = -1;
        Invalidate();
    }

    public void AddWidget(ObsLayoutWidget widget)
    {
        widgets.Add(widget.Clone());
        selectedIndex = widgets.Count - 1;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void DeleteSelected()
    {
        if (selectedIndex < 0 || selectedIndex >= widgets.Count) return;
        widgets.RemoveAt(selectedIndex);
        selectedIndex = Math.Min(selectedIndex, widgets.Count - 1);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void DuplicateSelected()
    {
        if (selectedIndex < 0 || selectedIndex >= widgets.Count) return;
        var clone = widgets[selectedIndex].Clone();
        clone.X = Math.Clamp(clone.X + 0.02, 0, 1.0 - clone.W);
        clone.Y = Math.Clamp(clone.Y + 0.02, 0, 1.0 - clone.H);
        widgets.Add(clone);
        selectedIndex = widgets.Count - 1;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void AlignToStream(StreamAlign align)
    {
        if (selectedIndex < 0 || selectedIndex >= widgets.Count) return;
        var w = widgets[selectedIndex];
        switch (align)
        {
            case StreamAlign.Left:    w.X = 0; break;
            case StreamAlign.CenterH: w.X = (1.0 - w.W) / 2.0; break;
            case StreamAlign.Right:   w.X = 1.0 - w.W; break;
            case StreamAlign.Top:     w.Y = 0; break;
            case StreamAlign.MiddleV: w.Y = (1.0 - w.H) / 2.0; break;
            case StreamAlign.Bottom:  w.Y = 1.0 - w.H; break;
        }
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void BringSelectedForward()
    {
        if (selectedIndex <= 0 || selectedIndex >= widgets.Count) return;
        (widgets[selectedIndex], widgets[selectedIndex - 1]) = (widgets[selectedIndex - 1], widgets[selectedIndex]);
        selectedIndex--;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SendSelectedBackward()
    {
        if (selectedIndex < 0 || selectedIndex >= widgets.Count - 1) return;
        (widgets[selectedIndex], widgets[selectedIndex + 1]) = (widgets[selectedIndex + 1], widgets[selectedIndex]);
        selectedIndex++;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void NotifySelectedChanged()
    {
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    // ── Coordinate helpers ────────────────────────────────────────────────

    private RectangleF StreamRect
    {
        get
        {
            var aspect = (float)StreamW / StreamH;
            var cW = Width - 2f;
            var cH = Height - 2f;
            float sW, sH;
            if (cW / cH > aspect) { sH = cH; sW = sH * aspect; }
            else { sW = cW; sH = sW / aspect; }
            return new RectangleF((Width - sW) / 2f, (Height - sH) / 2f, sW, sH);
        }
    }

    private RectangleF WidgetToScreen(ObsLayoutWidget w)
    {
        var s = StreamRect;
        return new RectangleF(
            s.X + (float)w.X * s.Width,
            s.Y + (float)w.Y * s.Height,
            (float)w.W * s.Width,
            (float)w.H * s.Height);
    }

    private void ScreenToWidget(RectangleF r, ObsLayoutWidget w)
    {
        var s = StreamRect;
        var x  = Math.Clamp((r.X - s.X) / s.Width,  0.0, 1.0);
        var y  = Math.Clamp((r.Y - s.Y) / s.Height, 0.0, 1.0);
        var ww = Math.Clamp(r.Width  / s.Width,  0.01, 1.0 - x);
        var wh = Math.Clamp(r.Height / s.Height, 0.01, 1.0 - y);

        if (SnapToGrid)
        {
            x  = Snap(x,  SnapStep);
            y  = Snap(y,  SnapStep);
            ww = Math.Max(SnapStep, Snap(ww, SnapStep));
            wh = Math.Max(SnapStep, Snap(wh, SnapStep));
        }

        w.X = x; w.Y = y; w.W = ww; w.H = wh;
    }

    private static double Snap(double v, double step) => Math.Round(v / step) * step;

    // ── Painting ──────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        var s = StreamRect;

        // Checkerboard to indicate transparency
        DrawCheckerboard(g, s);

        // Grid lines
        using var gridPen = new Pen(Color.FromArgb(22, 255, 255, 255));
        for (var i = 1; i < 8; i++)
        {
            var x = s.X + s.Width * i / 8f;
            g.DrawLine(gridPen, x, s.Y, x, s.Bottom);
        }
        for (var j = 1; j < 6; j++)
        {
            var y = s.Y + s.Height * j / 6f;
            g.DrawLine(gridPen, s.X, y, s.Right, y);
        }

        // Snap grid overlay
        if (SnapToGrid)
        {
            using var snapPen = new Pen(Color.FromArgb(14, 255, 255, 255));
            for (var i = 1; i < 40; i++)
            {
                var x = s.X + s.Width * i / 40f;
                g.DrawLine(snapPen, x, s.Y, x, s.Bottom);
            }
            for (var j = 1; j < 40; j++)
            {
                var y = s.Y + s.Height * j / 40f;
                g.DrawLine(snapPen, s.X, y, s.Right, y);
            }
        }

        // Stream border
        using var streamBorder = new Pen(Color.FromArgb(55, 255, 255, 255));
        g.DrawRectangle(streamBorder, s.X, s.Y, s.Width, s.Height);

        // Stream label
        using var labelFont = new Font("Segoe UI", 8F);
        using var labelBrush = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
        g.DrawString("1920 × 1080", labelFont, labelBrush, s.Right - 78, s.Bottom + 4);

        // Widgets (back to front, so index 0 is topmost visual)
        for (var i = widgets.Count - 1; i >= 0; i--)
            DrawWidget(g, s, i);

        // Selection handles on top
        if (selectedIndex >= 0 && selectedIndex < widgets.Count)
            DrawHandles(g, WidgetToScreen(widgets[selectedIndex]));
    }

    private static void DrawCheckerboard(Graphics g, RectangleF r)
    {
        const int cell = 12;
        var c1 = Color.FromArgb(22, 22, 28);
        var c2 = Color.FromArgb(30, 30, 38);
        var cols = (int)Math.Ceiling(r.Width  / cell);
        var rows = (int)Math.Ceiling(r.Height / cell);
        for (var row = 0; row < rows; row++)
        for (var col = 0; col < cols; col++)
        {
            using var b = new SolidBrush((row + col) % 2 == 0 ? c1 : c2);
            var cx = r.X + col * cell;
            var cy = r.Y + row * cell;
            var cw = Math.Min(cell, r.Right  - cx);
            var ch = Math.Min(cell, r.Bottom - cy);
            if (cw > 0 && ch > 0) g.FillRectangle(b, cx, cy, cw, ch);
        }
    }

    private void DrawWidget(Graphics g, RectangleF stream, int index)
    {
        var w = widgets[index];
        var r = WidgetToScreen(w);
        var isSelected = index == selectedIndex;

        var bg = WidgetBackColor(w.Type);
        var fillAlpha = isSelected ? 140 : 95;
        var radius = Math.Min(r.Width / 2f, Math.Min(r.Height / 2f, 7f));

        using var path = RoundedRect(r, radius);
        using var fillBrush = new SolidBrush(Color.FromArgb(fillAlpha, bg.R, bg.G, bg.B));
        g.FillPath(fillBrush, path);

        using var borderPen = isSelected
            ? new Pen(palette.AccentPrimaryColor, 1.5f)
            : new Pen(Color.FromArgb(110, bg.R, bg.G, bg.B), 1f);
        g.DrawPath(borderPen, path);

        // Left accent stripe
        if (r.Height > 8)
        {
            var stripeR = new RectangleF(r.X + 1, r.Y + radius, 3, r.Height - radius * 2);
            if (stripeR.Height > 0)
            {
                using var stripeB = new SolidBrush(Color.FromArgb(210, bg.R, bg.G, bg.B));
                g.FillRectangle(stripeB, stripeR);
            }
        }

        // Label
        var labelFontSize = Math.Max(6f, Math.Min(10f, r.Height * 0.26f));
        using var textFont  = new Font("Segoe UI", labelFontSize);
        using var textBrush = new SolidBrush(isSelected ? Color.White : Color.FromArgb(210, 255, 255, 255));
        var label = GetWidgetLabel(w);
        var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Near };
        g.DrawString(label, textFont, textBrush,
            new RectangleF(r.X + 8, r.Y + 3, r.Width - 12, r.Height * 0.60f), sf);

        // Pixel dimensions at bottom right
        if (r.Height > 20 && r.Width > 48)
        {
            var pw = (int)Math.Round(w.W * StreamW);
            var ph = (int)Math.Round(w.H * StreamH);
            var dimFontSize = Math.Max(5f, Math.Min(7.5f, r.Height * 0.18f));
            using var dimFont  = new Font("Segoe UI", dimFontSize);
            using var dimBrush = new SolidBrush(Color.FromArgb(100, 255, 255, 255));
            var dimSf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far };
            g.DrawString($"{pw}×{ph}px", dimFont, dimBrush,
                new RectangleF(r.X + 2, r.Y + r.Height * 0.40f, r.Width - 5, r.Height * 0.55f), dimSf);
        }
    }

    private static string GetWidgetLabel(ObsLayoutWidget w) => w.Type switch
    {
        ObsWidgetType.NowPlaying  => "♪ Now Playing",
        ObsWidgetType.Lyrics      => "✎ Lyrics",
        ObsWidgetType.Queue       => "≡ Queue",
        ObsWidgetType.Progress    => "▶ Progress Bar",
        ObsWidgetType.Visualizer  => $"◈ {PrettyVizKey(w.VizKey)}",
        ObsWidgetType.SongWarsBracket => "Song Wars Bracket",
        _                         => w.Type
    };

    private static string PrettyVizKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Visualizer";
        if (key.StartsWith(VisualizerChoice.InstalledPrefix, StringComparison.OrdinalIgnoreCase))
            return key[VisualizerChoice.InstalledPrefix.Length..];
        if (key.StartsWith(VisualizerChoice.BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
            return key[VisualizerChoice.BuiltInPrefix.Length..];
        return key;
    }

    private static Color WidgetBackColor(string type) => type switch
    {
        ObsWidgetType.NowPlaying  => Color.FromArgb(40,  90, 180),
        ObsWidgetType.Lyrics      => Color.FromArgb(100, 40, 150),
        ObsWidgetType.Queue       => Color.FromArgb(20, 120,  90),
        ObsWidgetType.Progress    => Color.FromArgb(180, 100,  20),
        ObsWidgetType.Visualizer  => Color.FromArgb(20,  140,  60),
        ObsWidgetType.SongWarsBracket => Color.FromArgb(190, 70,  70),
        _                         => Color.FromArgb(70, 70, 70)
    };

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d <= 0 || r.Width <= d || r.Height <= d)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void DrawHandles(Graphics g, RectangleF r)
    {
        // Dashed selection outline
        using var dashPen = new Pen(palette.AccentPrimaryColor, 1.5f);
        dashPen.DashStyle = DashStyle.Dash;
        dashPen.DashPattern = [4f, 3f];
        g.DrawRectangle(dashPen, r.X, r.Y, r.Width, r.Height);

        using var hBrush = new SolidBrush(Color.White);
        using var hPen   = new Pen(palette.AccentPrimaryColor, 1.5f);
        var hs = HandleSize;

        foreach (var pt in GetHandlePoints(r))
        {
            var hr = new RectangleF(pt.X - hs / 2f, pt.Y - hs / 2f, hs, hs);
            g.FillEllipse(hBrush, hr);
            g.DrawEllipse(hPen, hr);
        }
    }

    private static PointF[] GetHandlePoints(RectangleF r) =>
    [
        new(r.Left,  r.Top),    new(r.Right, r.Top),
        new(r.Left,  r.Bottom), new(r.Right, r.Bottom),
        new(r.Left + r.Width / 2f, r.Top),
        new(r.Left + r.Width / 2f, r.Bottom),
        new(r.Left,  r.Top + r.Height / 2f),
        new(r.Right, r.Top + r.Height / 2f)
    ];

    // ── Mouse interaction ─────────────────────────────────────────────────

    [Flags]
    private enum ResizeHandle { None=0, Left=1, Right=2, Top=4, Bottom=8 }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();

        dragStart = e.Location;

        // Check handles of selected widget first
        if (selectedIndex >= 0 && selectedIndex < widgets.Count)
        {
            var handle = HitTestHandles(e.Location, WidgetToScreen(widgets[selectedIndex]));
            if (handle != ResizeHandle.None)
            {
                isResizing = true;
                activeHandle = handle;
                dragOrigRect = WidgetToScreen(widgets[selectedIndex]);
                return;
            }
        }

        // Hit test widgets (front-to-back = index 0 first)
        for (var i = 0; i < widgets.Count; i++)
        {
            var r = WidgetToScreen(widgets[i]);
            if (r.Contains(e.Location))
            {
                selectedIndex = i;
                isDragging = true;
                dragOrigRect = r;
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        selectedIndex = -1;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateCursor(e.Location);

        if (selectedIndex < 0 || selectedIndex >= widgets.Count) return;
        var dx = e.Location.X - dragStart.X;
        var dy = e.Location.Y - dragStart.Y;
        var s = StreamRect;

        if (isDragging)
        {
            var newRect = new RectangleF(
                dragOrigRect.X + dx,
                dragOrigRect.Y + dy,
                dragOrigRect.Width,
                dragOrigRect.Height);
            newRect.X = Math.Clamp(newRect.X, s.X, s.Right  - newRect.Width);
            newRect.Y = Math.Clamp(newRect.Y, s.Y, s.Bottom - newRect.Height);
            ScreenToWidget(newRect, widgets[selectedIndex]);
            Invalidate();
        }
        else if (isResizing)
        {
            var r = dragOrigRect;
            if ((activeHandle & ResizeHandle.Left)   != 0) { r.X += dx; r.Width -= dx; }
            if ((activeHandle & ResizeHandle.Right)  != 0) { r.Width += dx; }
            if ((activeHandle & ResizeHandle.Top)    != 0) { r.Y += dy; r.Height -= dy; }
            if ((activeHandle & ResizeHandle.Bottom) != 0) { r.Height += dy; }
            if (r.Width  < 32) r.Width  = 32;
            if (r.Height < 20) r.Height = 20;
            r.X = Math.Max(s.X, r.X);
            r.Y = Math.Max(s.Y, r.Y);
            if (r.Right  > s.Right)  r.Width  = s.Right  - r.X;
            if (r.Bottom > s.Bottom) r.Height = s.Bottom - r.Y;
            ScreenToWidget(r, widgets[selectedIndex]);
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (isDragging || isResizing)
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        isDragging = false;
        isResizing = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
            DeleteSelected();
        else if (e.KeyCode == Keys.D && e.Control)
            DuplicateSelected();
    }

    private void UpdateCursor(Point pt)
    {
        if (selectedIndex >= 0 && selectedIndex < widgets.Count)
        {
            var h = HitTestHandles(pt, WidgetToScreen(widgets[selectedIndex]));
            Cursor = h switch
            {
                ResizeHandle.Left or ResizeHandle.Right => Cursors.SizeWE,
                ResizeHandle.Top  or ResizeHandle.Bottom => Cursors.SizeNS,
                ResizeHandle.Left | ResizeHandle.Top   or ResizeHandle.Right | ResizeHandle.Bottom => Cursors.SizeNWSE,
                ResizeHandle.Left | ResizeHandle.Bottom or ResizeHandle.Right | ResizeHandle.Top  => Cursors.SizeNESW,
                _ => HitTestWidgets(pt) >= 0 ? Cursors.SizeAll : Cursors.Default
            };
            return;
        }
        Cursor = HitTestWidgets(pt) >= 0 ? Cursors.SizeAll : Cursors.Default;
    }

    private int HitTestWidgets(Point pt)
    {
        for (var i = 0; i < widgets.Count; i++)
            if (WidgetToScreen(widgets[i]).Contains(pt)) return i;
        return -1;
    }

    private ResizeHandle HitTestHandles(Point pt, RectangleF r)
    {
        var points = GetHandlePoints(r);
        var flags  = new ResizeHandle[]
        {
            ResizeHandle.Left  | ResizeHandle.Top,
            ResizeHandle.Right | ResizeHandle.Top,
            ResizeHandle.Left  | ResizeHandle.Bottom,
            ResizeHandle.Right | ResizeHandle.Bottom,
            ResizeHandle.Top,
            ResizeHandle.Bottom,
            ResizeHandle.Left,
            ResizeHandle.Right
        };
        var hit = HandleSize + 3;
        for (var i = 0; i < points.Length; i++)
        {
            if (Math.Abs(pt.X - points[i].X) <= hit && Math.Abs(pt.Y - points[i].Y) <= hit)
                return flags[i];
        }
        return ResizeHandle.None;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}
