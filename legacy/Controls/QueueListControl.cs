using System.Drawing;
using System.IO;

namespace Spectralis;

internal sealed record QueueListItem(
    string Title,
    string? Subtitle,
    bool IsCurrent = false,
    bool IsPlaying = false);

internal sealed class QueueListControl : Control
{
    private const int ItemH = 42;
    private const int ScrollW = 4;
    private const int NumColW = 32;
    private const int PlayIconW = 14;
    private const int LeftPad = 10;
    private const int RightPad = 8;

    private int _scrollY;
    private int _selectedIndex = -1;
    private bool _scrollDragging;
    private int _scrollDragStartY;
    private int _scrollDragStartOffset;

    public PlayQueue? Queue { get; set; }
    public AudioEngine? Engine { get; set; }
    public ThemePalette? Theme { get; set; }
    public IReadOnlyList<QueueListItem>? DisplayItems { get; set; }

    public event EventHandler<int>? ItemActivated;
    public event EventHandler<int>? ItemDeleteRequested;
    public event EventHandler<(int Index, Point Location)>? ItemRightClicked;

    public QueueListControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        TabStop = false;
        Cursor = Cursors.Default;
    }

    public void ScrollToIndex(int index)
    {
        if (ItemCount == 0 || index < 0) return;
        var top = index * ItemH;
        var bottom = top + ItemH;
        if (top < _scrollY) _scrollY = top;
        else if (bottom > _scrollY + Height) _scrollY = bottom - Height;
        _scrollY = ClampScroll(_scrollY);
        Invalidate();
    }

    private bool IsDisplayMode => DisplayItems is not null;
    private int ItemCount => DisplayItems?.Count ?? Queue?.Count ?? 0;
    private int TotalH => ItemCount * ItemH;
    private int MaxScroll => Math.Max(0, TotalH - Height);
    private int ClampScroll(int v) => Math.Clamp(v, 0, MaxScroll);

    private int IndexAt(int pixelY)
    {
        var i = (pixelY + _scrollY) / ItemH;
        return i < 0 || i >= ItemCount ? -1 : i;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _scrollY = ClampScroll(_scrollY - (e.Delta / 120) * ItemH);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        // Scrollbar hit area
        if (TotalH > Height && e.X >= Width - ScrollW - 4)
        {
            _scrollDragging = true;
            _scrollDragStartY = e.Y;
            _scrollDragStartOffset = _scrollY;
            Capture = true;
            return;
        }

        var idx = IndexAt(e.Y);
        if (idx < 0) return;
        _selectedIndex = idx;
        Invalidate();

        if (!IsDisplayMode && e.Button == MouseButtons.Right)
            ItemRightClicked?.Invoke(this, (idx, e.Location));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_scrollDragging) return;
        var trackH = Height - ThumbH;
        if (trackH <= 0 || MaxScroll == 0) return;
        var delta = e.Y - _scrollDragStartY;
        _scrollY = ClampScroll(_scrollDragStartOffset + (int)((float)delta / trackH * MaxScroll));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e) { _scrollDragging = false; Capture = false; }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (IsDisplayMode)
            return;

        var idx = IndexAt(e.Y);
        if (idx >= 0)
        {
            _selectedIndex = idx;
            ItemActivated?.Invoke(this, idx);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Queue is null || IsDisplayMode) return;
        switch (e.KeyCode)
        {
            case Keys.Up when _selectedIndex > 0:
                _selectedIndex--;
                ScrollToIndex(_selectedIndex);
                e.Handled = true;
                break;
            case Keys.Down when Queue.Count > 0 && _selectedIndex < Queue.Count - 1:
                _selectedIndex++;
                ScrollToIndex(_selectedIndex);
                e.Handled = true;
                break;
            case Keys.Return when _selectedIndex >= 0:
                ItemActivated?.Invoke(this, _selectedIndex);
                e.Handled = true;
                break;
            case Keys.Delete when _selectedIndex >= 0:
                ItemDeleteRequested?.Invoke(this, _selectedIndex);
                e.Handled = true;
                break;
        }
    }

    // ── Scrollbar geometry ─────────────────────────────────────────────────

    private int ThumbH => TotalH <= Height ? Height : Math.Max(28, Height * Height / TotalH);
    private int ThumbY => MaxScroll == 0 ? 0 : (int)((float)_scrollY / MaxScroll * (Height - ThumbH));

    // ── Paint ──────────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (Theme is null || (Queue is null && DisplayItems is null))
        {
            g.Clear(BackColor);
            return;
        }

        g.Clear(BackColor);

        var showScroll = TotalH > Height;
        var drawW = showScroll ? Width - ScrollW - 2 : Width;
        var displayItems = DisplayItems;
        var items = Queue?.Items ?? [];
        var currentIdx = displayItems is null ? Queue?.CurrentIndex ?? -1 : -1;
        var isPlaying = displayItems is null ? Engine?.IsPlaying ?? false : false;
        var currentDisplay = Engine?.CurrentTrack?.DisplayName;

        var first = Math.Max(0, _scrollY / ItemH);
        var last = Math.Min(ItemCount - 1, (_scrollY + Height + ItemH - 1) / ItemH);

        using var normalFont = new Font(Font, FontStyle.Regular);
        using var boldFont   = new Font(Font, FontStyle.Bold);
        using var sf = new StringFormat
        {
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter,
            LineAlignment = StringAlignment.Center
        };
        using var sfNum = new StringFormat
        {
            Alignment = StringAlignment.Far,
            FormatFlags = StringFormatFlags.NoWrap,
            LineAlignment = StringAlignment.Center
        };

        for (var i = first; i <= last; i++)
        {
            var iY = i * ItemH - _scrollY;
            var displayItem = displayItems is not null ? displayItems[i] : null;
            var isCurrent = displayItem?.IsCurrent ?? i == currentIdx;
            var isSelected = i == _selectedIndex;
            var itemRect = new Rectangle(0, iY, drawW, ItemH);
            var rowIsPlaying = displayItem?.IsPlaying ?? (isCurrent && isPlaying);

            // Background
            if (isCurrent)
            {
                var a = Theme.AccentPrimaryColor;
                using var bg = new SolidBrush(Color.FromArgb(32, a.R, a.G, a.B));
                g.FillRectangle(bg, itemRect);
            }
            else if (isSelected)
            {
                using var bg = new SolidBrush(Theme.SurfaceAltBackColor);
                g.FillRectangle(bg, itemRect);
            }

            // Left accent bar
            if (isCurrent)
            {
                using var bar = new SolidBrush(Theme.AccentPrimaryColor);
                g.FillRectangle(bar, 0, iY, 3, ItemH);
            }

            var name = displayItem?.Title;
            var subtitle = displayItem?.Subtitle;
            if (name is null)
            {
                var path = items[i];
                name = isCurrent && currentDisplay is not null
                    ? currentDisplay
                    : BuildQueueItemTitle(path);
                subtitle ??= BuildQueueItemSubtitle(path);
            }

            var font = isCurrent ? boldFont : normalFont;
            var numColor = isCurrent ? Theme.AccentPrimaryColor : Theme.TextMutedColor;
            var nameColor = isCurrent ? Theme.TextPrimaryColor : Theme.TextSecondaryColor;

            // Number
            using (var br = new SolidBrush(numColor))
                g.DrawString($"{i + 1}", font, br,
                    new RectangleF(LeftPad, iY, NumColW, ItemH), sfNum);

            // Play chevron / indicator
            var iconX = LeftPad + NumColW + 2;
            if (isCurrent && rowIsPlaying)
            {
                using var br = new SolidBrush(Theme.AccentPrimaryColor);
                sf.Alignment = StringAlignment.Near;
                g.DrawString("▶", font, br, new RectangleF(iconX, iY, PlayIconW, ItemH), sf);
            }

            // Track name (single-line, ellipsis)
            var nameX = iconX + PlayIconW + 2;
            var nameW = drawW - nameX - RightPad;
            sf.Alignment = StringAlignment.Near;
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                sf.LineAlignment = StringAlignment.Near;
                using (var br = new SolidBrush(nameColor))
                    g.DrawString(name, font, br,
                        new RectangleF(nameX, iY + 4, nameW, 20), sf);
                using var smallFont = new Font(Font.FontFamily, Math.Max(7F, Font.Size - 1F), FontStyle.Regular, GraphicsUnit.Point);
                using var subtitleBrush = new SolidBrush(isCurrent ? Theme.TextSecondaryColor : Theme.TextMutedColor);
                g.DrawString(subtitle, smallFont, subtitleBrush,
                    new RectangleF(nameX, iY + 22, nameW, 16), sf);
                sf.LineAlignment = StringAlignment.Center;
            }
            else
            {
                using var br = new SolidBrush(nameColor);
                g.DrawString(name, font, br,
                    new RectangleF(nameX, iY, nameW, ItemH), sf);
            }

            // Thin separator
            if (!isCurrent && !isSelected && i < last)
            {
                using var sep = new Pen(Color.FromArgb(18, Theme.TextMutedColor));
                g.DrawLine(sep, LeftPad, iY + ItemH - 1, drawW - RightPad, iY + ItemH - 1);
            }
        }

        // Scrollbar
        if (showScroll)
        {
            var sbX = Width - ScrollW;
            using (var trackBr = new SolidBrush(Color.FromArgb(16, Theme.TextMutedColor)))
                g.FillRectangle(trackBr, sbX, 0, ScrollW, Height);
            using (var thumbBr = new SolidBrush(Color.FromArgb(70, Theme.TextSoftColor)))
                g.FillRectangle(thumbBr, sbX + 1, ThumbY, ScrollW - 1, ThumbH);
        }
    }

    private static string BuildQueueItemTitle(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(':', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 ? $"{parts[1]} {parts[2]}" : "Spotify";
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            if (IsYouTubeHost(uri.Host))
            {
                var videoId = GetQueryParameter(uri, "v") ??
                    uri.AbsolutePath
                        .Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .LastOrDefault();
                return string.IsNullOrWhiteSpace(videoId)
                    ? "YouTube"
                    : $"YouTube {Uri.UnescapeDataString(videoId)}";
            }

            var last = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            return string.IsNullOrWhiteSpace(last)
                ? uri.Host
                : Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(last));
        }

        return Path.GetFileNameWithoutExtension(trimmed);
    }

    private static string? BuildQueueItemSubtitle(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
            return "Spotify";

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https"
            ? uri.Host
            : null;
    }

    private static bool IsYouTubeHost(string host) =>
        host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".youtube-nocookie.com", StringComparison.OrdinalIgnoreCase);

    private static string? GetQueryParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 0 ||
                !string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return parts.Length > 1
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                : "";
        }

        return null;
    }
}
