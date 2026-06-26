using System.Drawing;

namespace Spectralis;

internal sealed class ScriptEditorControl : UserControl
{
    private readonly RichTextBox _editor;
    private readonly Label _lblError;
    private bool _suppressChange;

    public event EventHandler? ScriptChanged;

    public ScriptEditorControl()
    {
        _editor = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10f),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            AcceptsTab = true,
        };
        _editor.TextChanged += (_, _) =>
        {
            if (!_suppressChange) ScriptChanged?.Invoke(this, EventArgs.Empty);
        };

        _lblError = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 8.5f),
            Padding = new Padding(6, 4, 6, 4),
            Text = "",
        };

        Controls.Add(_editor);
        Controls.Add(_lblError);
    }

    public string Script
    {
        get => _editor.Text;
        set
        {
            _suppressChange = true;
            _editor.Text = value;
            _suppressChange = false;
        }
    }

    public void ShowError(string? error)
    {
        _lblError.Text = string.IsNullOrWhiteSpace(error) ? "" : $"⚠ {error}";
        _lblError.ForeColor = string.IsNullOrWhiteSpace(error)
            ? Color.FromArgb(100, 100, 100)
            : Color.FromArgb(255, 100, 80);
    }

    public void ApplyTheme(ThemePalette theme)
    {
        BackColor = theme.WindowBackColor;
        _editor.BackColor = theme.SurfaceAltBackColor;
        _editor.ForeColor = theme.TextPrimaryColor;
        _lblError.BackColor = theme.SurfaceBackColor;
        _lblError.ForeColor = Color.FromArgb(100, 100, 100);
    }
}
