using System.Drawing;

namespace Spectralis;

internal sealed class ThemeStatusStripRenderer : ToolStripProfessionalRenderer
{
    private readonly Color backgroundColor;
    private readonly Color borderColor;

    public ThemeStatusStripRenderer(Color backgroundColor, Color borderColor)
        : base(new ThemeColorTable(backgroundColor, backgroundColor, backgroundColor, borderColor))
    {
        this.backgroundColor = backgroundColor;
        this.borderColor = borderColor;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(backgroundColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);

        using var borderPen = new Pen(Color.FromArgb(48, borderColor), 1f);
        e.Graphics.DrawLine(
            borderPen,
            e.AffectedBounds.Left,
            e.AffectedBounds.Top,
            e.AffectedBounds.Right,
            e.AffectedBounds.Top);
    }

    protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e) { }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { }
}

internal sealed class ThemeMenuStripRenderer : ToolStripProfessionalRenderer
{
    private readonly Color backgroundColor;
    private readonly Color imageMarginColor;

    public ThemeMenuStripRenderer(Color backgroundColor, Color imageMarginColor, Color selectionColor, Color borderColor)
        : base(new ThemeColorTable(backgroundColor, imageMarginColor, selectionColor, borderColor))
    {
        this.backgroundColor = backgroundColor;
        this.imageMarginColor = imageMarginColor;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(backgroundColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(imageMarginColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
}

internal sealed class ThemeColorTable : ProfessionalColorTable
{
    private readonly Color backgroundColor;
    private readonly Color dropDownColor;
    private readonly Color selectionColor;
    private readonly Color borderColor;

    public ThemeColorTable(Color backgroundColor, Color dropDownColor, Color selectionColor, Color borderColor)
    {
        this.backgroundColor = backgroundColor;
        this.dropDownColor = dropDownColor;
        this.selectionColor = selectionColor;
        this.borderColor = borderColor;
    }

    public override Color StatusStripGradientBegin => backgroundColor;
    public override Color StatusStripGradientEnd => backgroundColor;
    public override Color MenuStripGradientBegin => backgroundColor;
    public override Color MenuStripGradientEnd => backgroundColor;
    public override Color ToolStripDropDownBackground => dropDownColor;
    public override Color ImageMarginGradientBegin => dropDownColor;
    public override Color ImageMarginGradientMiddle => dropDownColor;
    public override Color ImageMarginGradientEnd => dropDownColor;
    public override Color MenuItemSelected => selectionColor;
    public override Color MenuItemBorder => borderColor;
    public override Color MenuBorder => borderColor;
    public override Color MenuItemSelectedGradientBegin => selectionColor;
    public override Color MenuItemSelectedGradientEnd => selectionColor;
    public override Color MenuItemPressedGradientBegin => dropDownColor;
    public override Color MenuItemPressedGradientMiddle => dropDownColor;
    public override Color MenuItemPressedGradientEnd => dropDownColor;
    public override Color SeparatorDark => borderColor;
    public override Color SeparatorLight => dropDownColor;
}
