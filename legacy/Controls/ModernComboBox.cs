using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Spectralis;

public sealed class ModernComboBox : ComboBox
{
    private Color surfaceColor = Color.FromArgb(35, 30, 36);
    private Color surfaceHoverColor = Color.FromArgb(41, 35, 42);
    private Color surfaceOpenColor = Color.FromArgb(44, 37, 44);
    private Color surfaceActiveColor = Color.FromArgb(52, 43, 49);
    private Color borderColor = Color.FromArgb(82, 68, 58);
    private Color borderHoverColor = Color.FromArgb(129, 98, 74);
    private Color buttonColor = Color.FromArgb(45, 38, 44);
    private Color buttonActiveColor = Color.FromArgb(60, 49, 55);
    private Color textColor = Color.FromArgb(242, 236, 228);
    private Color mutedTextColor = Color.FromArgb(166, 149, 136);
    private Color caretColor = Color.FromArgb(228, 176, 106);
    private float cornerRadius = 6f;

    private bool isHovering;
    private IntPtr listBackgroundBrush;
    private Color listBackgroundBrushColor = Color.Empty;
    private readonly DropDownListNativeWindow dropDownListWindow = new();

    public ModernComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        IntegralHeight = false;
        MaxDropDownItems = 10;
        ItemHeight = 34;

        BackColor = surfaceColor;
        ForeColor = textColor;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseListBackgroundBrush();
            dropDownListWindow.Dispose();
        }

        base.Dispose(disposing);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new DrawMode DrawMode
    {
        get => base.DrawMode;
        set => base.DrawMode = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new ComboBoxStyle DropDownStyle
    {
        get => base.DropDownStyle;
        set => base.DropDownStyle = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceColor
    {
        get => surfaceColor;
        set
        {
            surfaceColor = value;
            BackColor = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceHoverColor
    {
        get => surfaceHoverColor;
        set { surfaceHoverColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceOpenColor
    {
        get => surfaceOpenColor;
        set { surfaceOpenColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceActiveColor
    {
        get => surfaceActiveColor;
        set { surfaceActiveColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor
    {
        get => borderColor;
        set { borderColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderHoverColor
    {
        get => borderHoverColor;
        set { borderHoverColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ButtonColor
    {
        get => buttonColor;
        set { buttonColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ButtonActiveColor
    {
        get => buttonActiveColor;
        set { buttonActiveColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TextColor
    {
        get => textColor;
        set
        {
            textColor = value;
            ForeColor = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color MutedTextColor
    {
        get => mutedTextColor;
        set { mutedTextColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CaretColor
    {
        get => caretColor;
        set { caretColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float CornerRadius
    {
        get => cornerRadius;
        set
        {
            cornerRadius = Math.Max(1f, value);
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        isHovering = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        isHovering = false;
        Invalidate();
    }

    protected override void OnDropDown(EventArgs e)
    {
        base.OnDropDown(e);
        UpdateDropDownHeight();
        EnsureDropDownWindowTheme();
        Invalidate();
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        base.OnDropDownClosed(e);
        dropDownListWindow.Detach();
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyNativeTheme();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0)
            return;

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var isEditPortion =
            (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit ||
            (!DroppedDown && e.Bounds.Height >= Height - 4);
        var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected && !isEditPortion;
        var itemText = e.Index >= 0
            ? GetItemText(Items[e.Index])
            : SelectedIndex >= 0 ? GetItemText(Items[SelectedIndex]) : Text;

        var backgroundColor = !Enabled
            ? Color.FromArgb(42, 38, 42)
            : isEditPortion
                ? GetSurfaceColor()
                : isSelected ? surfaceActiveColor : surfaceColor;

        using (var backgroundBrush = new SolidBrush(backgroundColor))
        {
            graphics.FillRectangle(backgroundBrush, e.Bounds);
        }

        if (!isEditPortion)
        {
            using var dividerPen = new Pen(Color.FromArgb(34, 255, 255, 255), 1f);
            graphics.DrawLine(dividerPen, e.Bounds.Left + 10, e.Bounds.Bottom - 1, e.Bounds.Right - 10, e.Bounds.Bottom - 1);
        }

        var textRect = Rectangle.Inflate(e.Bounds, -14, 0);
        if (isEditPortion)
            textRect.Width -= 30;

        TextRenderer.DrawText(
            graphics,
            itemText,
            Font,
            textRect,
            Enabled ? textColor : mutedTextColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_CTLCOLORLISTBOX = 0x0134;
        if (m.Msg == WM_CTLCOLORLISTBOX)
        {
            var background = DroppedDown ? surfaceOpenColor : surfaceColor;
            EnsureListBackgroundBrush(background);
            _ = SetBkColor(m.WParam, ToColorRef(background));
            _ = SetTextColor(m.WParam, ToColorRef(Enabled ? textColor : mutedTextColor));
            m.Result = listBackgroundBrush;
            return;
        }

        base.WndProc(ref m);

        const int WM_PAINT = 0x000F;
        if (m.Msg == WM_PAINT && IsHandleCreated)
            PaintChrome();
    }

    private void PaintChrome()
    {
        using var graphics = Graphics.FromHwnd(Handle);

        // Cover the native control's square corners with the parent background before any smooth drawing.
        var parentColor = Parent?.BackColor ?? BackColor;
        var cr = (int)Math.Ceiling(cornerRadius) + 1;
        using (var cornerBrush = new SolidBrush(parentColor))
        {
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.FillRectangle(cornerBrush, 0, 0, cr, cr);
            graphics.FillRectangle(cornerBrush, Width - cr, 0, cr, cr);
            graphics.FillRectangle(cornerBrush, 0, Height - cr, cr, cr);
            graphics.FillRectangle(cornerBrush, Width - cr, Height - cr, cr, cr);
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var outer = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        if (outer.Width <= 0 || outer.Height <= 0)
            return;

        var buttonRect = new RectangleF(Math.Max(outer.Left, outer.Right - 34f), outer.Top + 1f, 33f, outer.Height - 2f);

        using (var clipPath = CreateRoundedPath(outer, cornerRadius))
        {
            graphics.SetClip(clipPath);
            using var buttonBrush = new SolidBrush(DroppedDown ? buttonActiveColor : buttonColor);
            graphics.FillRectangle(buttonBrush, buttonRect);
            graphics.ResetClip();
        }

        using (var separatorPen = new Pen(Color.FromArgb(52, 255, 255, 255), 1f))
        {
            graphics.DrawLine(separatorPen, buttonRect.Left, outer.Top + 6f, buttonRect.Left, outer.Bottom - 6f);
        }

        using (var caretBrush = new SolidBrush(Enabled ? caretColor : mutedTextColor))
        using (var caretPath = new GraphicsPath())
        {
            var centerX = buttonRect.Left + (buttonRect.Width / 2f);
            var centerY = buttonRect.Top + (buttonRect.Height / 2f) + 1f;
            caretPath.AddPolygon(
                [
                    new PointF(centerX - 4.5f, centerY - 2.5f),
                    new PointF(centerX + 4.5f, centerY - 2.5f),
                    new PointF(centerX, centerY + 3.5f)
                ]);
            graphics.FillPath(caretBrush, caretPath);
        }

        using (var borderPath = CreateRoundedPath(outer, cornerRadius))
        using (var borderPen = new Pen(GetBorderColor(), 1f))
        {
            graphics.DrawPath(borderPen, borderPath);
        }
    }

    private Color GetSurfaceColor()
    {
        if (!Enabled)
            return Color.FromArgb(42, 38, 42);

        if (DroppedDown)
            return surfaceOpenColor;

        if (Focused || isHovering)
            return surfaceHoverColor;

        return surfaceColor;
    }

    private Color GetBorderColor()
    {
        if (!Enabled)
            return Color.FromArgb(70, 60, 54);

        return Focused || DroppedDown || isHovering
            ? borderHoverColor
            : borderColor;
    }

    private void ApplyNativeTheme()
    {
        if (!IsHandleCreated)
            return;

        _ = SetWindowTheme(Handle, string.Empty, string.Empty);
    }

    private void UpdateDropDownHeight()
    {
        var visibleItemCount = Math.Max(1, Math.Min(MaxDropDownItems, Items.Count == 0 ? 1 : Items.Count));
        DropDownHeight = Math.Max(38, (visibleItemCount * ItemHeight) + 4);
    }

    private void EnsureDropDownWindowTheme()
    {
        if (!IsHandleCreated)
            return;

        ApplyNativeTheme();

        var info = new ComboBoxInfo
        {
            cbSize = Marshal.SizeOf<ComboBoxInfo>()
        };

        if (!GetComboBoxInfo(Handle, ref info) || info.hwndList == IntPtr.Zero)
            return;

        dropDownListWindow.Attach(info.hwndList);
        dropDownListWindow.ApplyTheme(
            DroppedDown ? surfaceOpenColor : surfaceColor,
            GetBorderColor());
    }

    private static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var safeRadius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        if (safeRadius < 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = safeRadius * 2f;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComboBoxInfo(IntPtr hwndCombo, ref ComboBoxInfo pcbi);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string? pszSubIdList);

    private sealed class DropDownListNativeWindow : NativeWindow, IDisposable
    {
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PAINT = 0x000F;

        private IntPtr backgroundBrush;
        private Color borderColor = Color.Black;
        private Color backgroundColor = Color.Black;

        public void Attach(IntPtr handle)
        {
            if (Handle == handle)
                return;

            Detach();
            AssignHandle(handle);
            _ = SetWindowTheme(handle, string.Empty, string.Empty);
        }

        public void ApplyTheme(Color background, Color border)
        {
            backgroundColor = background;
            borderColor = border;
            RecreateBrush();

            if (Handle != IntPtr.Zero)
                _ = InvalidateRect(Handle, IntPtr.Zero, false);
        }

        public void Detach()
        {
            if (Handle != IntPtr.Zero)
                ReleaseHandle();

            ReleaseBrush();
        }

        public void Dispose()
        {
            Detach();
            GC.SuppressFinalize(this);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_ERASEBKGND:
                    if (backgroundBrush != IntPtr.Zero && GetClientRect(Handle, out var rect))
                    {
                        _ = FillRect(m.WParam, ref rect, backgroundBrush);
                        m.Result = (IntPtr)1;
                        return;
                    }
                    break;

                case WM_PAINT:
                    base.WndProc(ref m);
                    DrawBorder();
                    return;
            }

            base.WndProc(ref m);
        }

        private void DrawBorder()
        {
            if (Handle == IntPtr.Zero || !GetClientRect(Handle, out var rect))
                return;

            using var graphics = Graphics.FromHwnd(Handle);
            using var pen = new Pen(borderColor);
            graphics.DrawRectangle(
                pen,
                Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right - 1, rect.Bottom - 1));
        }

        private void RecreateBrush()
        {
            ReleaseBrush();
            backgroundBrush = CreateSolidBrush(ToColorRef(backgroundColor));
        }

        private void ReleaseBrush()
        {
            if (backgroundBrush == IntPtr.Zero)
                return;

            _ = DeleteObject(backgroundBrush);
            backgroundBrush = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, [In] ref Rect lprc, IntPtr hbr);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int colorRef);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern int SetBkColor(IntPtr hdc, int colorRef);

    [DllImport("gdi32.dll")]
    private static extern int SetTextColor(IntPtr hdc, int colorRef);

    private void EnsureListBackgroundBrush(Color color)
    {
        if (listBackgroundBrush != IntPtr.Zero && listBackgroundBrushColor.ToArgb() == color.ToArgb())
            return;

        ReleaseListBackgroundBrush();
        listBackgroundBrush = CreateSolidBrush(ToColorRef(color));
        listBackgroundBrushColor = color;
    }

    private void ReleaseListBackgroundBrush()
    {
        if (listBackgroundBrush == IntPtr.Zero)
            return;

        _ = DeleteObject(listBackgroundBrush);
        listBackgroundBrush = IntPtr.Zero;
        listBackgroundBrushColor = Color.Empty;
    }

    private static int ToColorRef(Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public int cbSize;
        public Rect rcItem;
        public Rect rcButton;
        public int stateButton;
        public IntPtr hwndCombo;
        public IntPtr hwndItem;
        public IntPtr hwndList;
    }
}
