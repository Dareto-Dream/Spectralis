using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace Spectralis;

internal sealed class CapsuleStoryControl : Control
{
    private CapsuleStoryDocument? story;
    private int sceneIndex;
    private Image? sceneImage;
    private ThemePalette palette = ThemePalette.Create(ThemeMode.Dark, ThemeAccent.Ocean);

    public CapsuleStoryControl()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable,
            true);

        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public bool HasStory => story?.Scenes.Count > 0;

    public event EventHandler? StoryCompleted;

    public void ApplyTheme(ThemePalette themePalette)
    {
        palette = themePalette;
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        Invalidate();
    }

    public void LoadStory(CapsuleStoryDocument document)
    {
        story = document;
        sceneIndex = 0;
        LoadSceneImage();
        Visible = HasStory;
        Invalidate();
    }

    public void Clear()
    {
        story = null;
        sceneIndex = 0;
        DisposeSceneImage();
        Visible = false;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Advance();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode is Keys.Space or Keys.Enter or Keys.Right)
        {
            Advance();
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var background = new LinearGradientBrush(ClientRectangle, palette.WindowBackColor, palette.SurfaceBackColor, 90f))
            graphics.FillRectangle(background, ClientRectangle);

        var current = CurrentScene;
        if (current is null)
            return;

        DrawSceneImage(graphics);
        DrawExplainerPanel(graphics, current);
    }

    private CapsuleStoryScene? CurrentScene =>
        story is { Scenes.Count: > 0 } document && sceneIndex >= 0 && sceneIndex < document.Scenes.Count
            ? document.Scenes[sceneIndex]
            : null;

    private void Advance()
    {
        if (story is not { Scenes.Count: > 0 } document)
            return;

        if (sceneIndex < document.Scenes.Count - 1)
        {
            sceneIndex++;
            LoadSceneImage();
            Invalidate();
            return;
        }

        StoryCompleted?.Invoke(this, EventArgs.Empty);
        Clear();
    }

    private void LoadSceneImage()
    {
        DisposeSceneImage();

        var current = CurrentScene;
        if (current?.ImageBytes is not { Length: > 0 } bytes)
            return;

        try
        {
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
            sceneImage = new Bitmap(image);
        }
        catch
        {
            sceneImage = null;
        }
    }

    private void DisposeSceneImage()
    {
        sceneImage?.Dispose();
        sceneImage = null;
    }

    private void DrawSceneImage(Graphics graphics)
    {
        if (sceneImage is null)
            return;

        var bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var maxWidth = Math.Max(120, bounds.Width / 2);
        var maxHeight = Math.Max(120, bounds.Height - 120);
        var scale = Math.Min(maxWidth / (float)sceneImage.Width, maxHeight / (float)sceneImage.Height);
        scale = Math.Min(scale, 1.8f);
        var width = (int)Math.Round(sceneImage.Width * scale);
        var height = (int)Math.Round(sceneImage.Height * scale);
        var x = Math.Max(20, bounds.Width - width - 48);
        var y = Math.Max(12, bounds.Height - height - 84);

        graphics.DrawImage(sceneImage, new Rectangle(x, y, width, height));
    }

    private void DrawExplainerPanel(Graphics graphics, CapsuleStoryScene scene)
    {
        var bounds = ClientRectangle;
        var padding = Math.Max(18, Math.Min(28, bounds.Width / 36));
        var boxHeight = Math.Clamp(bounds.Height / 3, 130, 230);
        var box = new Rectangle(
            padding,
            bounds.Height - boxHeight - padding,
            Math.Max(1, bounds.Width - padding * 2),
            boxHeight);

        using var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
        using var fill = new SolidBrush(Color.FromArgb(224, palette.SurfaceBackColor));
        using var border = new Pen(Color.FromArgb(180, palette.BorderStrongColor), 1f);
        using var accent = new Pen(Color.FromArgb(220, palette.AccentPrimaryColor), 2f);

        var shadowBox = box;
        shadowBox.Offset(0, 4);
        FillRoundRect(graphics, shadow, shadowBox, 8);
        FillRoundRect(graphics, fill, box, 8);
        DrawRoundRect(graphics, border, box, 8);
        graphics.DrawLine(accent, box.Left + 16, box.Top + 1, box.Right - 16, box.Top + 1);

        var speaker = string.IsNullOrWhiteSpace(scene.Speaker) ? "Story explainer" : scene.Speaker.Trim();
        using var speakerFont = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var textFont = new Font("Segoe UI", 13.5f, FontStyle.Regular, GraphicsUnit.Point);
        using var hintFont = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);

        var speakerRect = new Rectangle(box.Left + 20, box.Top + 14, box.Width - 40, 24);
        TextRenderer.DrawText(
            graphics,
            speaker,
            speakerFont,
            speakerRect,
            palette.AccentPrimaryColor,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var textRect = new Rectangle(box.Left + 20, speakerRect.Bottom + 8, box.Width - 40, box.Height - 72);
        TextRenderer.DrawText(
            graphics,
            scene.Text,
            textFont,
            textRect,
            palette.TextPrimaryColor,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

        var progressText = story is { Scenes.Count: > 1 } document
            ? $"{sceneIndex + 1}/{document.Scenes.Count}  Click for next"
            : "Click to close";
        var hintRect = new Rectangle(box.Left + 20, box.Bottom - 28, box.Width - 40, 18);
        TextRenderer.DrawText(
            graphics,
            progressText,
            hintFont,
            hintRect,
            palette.TextMutedColor,
            TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private static void FillRoundRect(Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        using var path = CreateRoundRect(rectangle, radius);
        graphics.FillPath(brush, path);
    }

    private static void DrawRoundRect(Graphics graphics, Pen pen, Rectangle rectangle, int radius)
    {
        using var path = CreateRoundRect(rectangle, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundRect(Rectangle rectangle, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeSceneImage();

        base.Dispose(disposing);
    }
}
