using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace Spectralis;

internal enum LegalDocumentKind
{
    TermsOfService,
    PrivacyPolicy
}

internal sealed class LegalDocumentDialog : Form
{
    private const string LegalPageFileName = "legal.html";

    private readonly ThemePalette palette;
    private readonly LegalDocumentKind documentKind;
    private readonly RichTextBox documentBox;
    private readonly ModernButton btnOpenWebPage;
    private readonly ModernButton btnCopy;
    private readonly ModernButton btnClose;

    public LegalDocumentDialog(ThemePalette palette, LegalDocumentKind documentKind)
    {
        this.palette = palette;
        this.documentKind = documentKind;

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(760, 640);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(560, 460);
        Padding = new Padding(22);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = DocumentTitle(documentKind);

        documentBox = new RichTextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            DetectUrls = true,
            Dock = DockStyle.Fill,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Text = LoadDocumentText(documentKind),
            WordWrap = true
        };
        documentBox.LinkClicked += (_, e) => OpenUrl(e.LinkText);

        btnOpenWebPage = new ModernButton
        {
            Size = new Size(142, 38),
            Text = "Open Legal Page"
        };
        btnOpenWebPage.Click += (_, _) => OpenLegalPage();

        btnCopy = new ModernButton
        {
            Size = new Size(112, 38),
            Text = "Copy"
        };
        btnCopy.Click += (_, _) => Clipboard.SetText(documentBox.Text);

        btnClose = new ModernButton
        {
            Size = new Size(112, 38),
            Text = "Close"
        };
        btnClose.Click += (_, _) => Close();

        Controls.Add(CreateLayout());
        ApplyTheme();
    }

    private Control CreateLayout()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle());
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle());

        var title = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 14),
            Text = DocumentTitle(documentKind)
        };

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 16, 0, 0),
            Padding = Padding.Empty,
            WrapContents = false
        };
        btnClose.Margin = Padding.Empty;
        btnCopy.Margin = new Padding(0, 0, 10, 0);
        btnOpenWebPage.Margin = new Padding(0, 0, 10, 0);
        footer.Controls.Add(btnClose);
        footer.Controls.Add(btnCopy);
        footer.Controls.Add(btnOpenWebPage);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(documentBox, 0, 1);
        root.Controls.Add(footer, 0, 2);
        return root;
    }

    private void ApplyTheme()
    {
        WindowChromeStyler.ApplyTheme(this, palette);
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;

        foreach (Control control in GetAllControls(this))
        {
            control.BackColor = palette.WindowBackColor;
            if (control is Label label)
                label.ForeColor = palette.TextPrimaryColor;
        }

        documentBox.BackColor = palette.SurfaceBackColor;
        documentBox.ForeColor = palette.TextSecondaryColor;
        documentBox.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        ThemeControlStyler.ApplyGhostButtonTheme(btnOpenWebPage, palette, palette.AccentSoftColor);
        ThemeControlStyler.ApplyGhostButtonTheme(btnCopy, palette, palette.AccentSoftColor);
        ThemeControlStyler.ApplyGhostButtonTheme(btnClose, palette, palette.BorderStrongColor);
    }

    private static IEnumerable<Control> GetAllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in GetAllControls(child))
                yield return descendant;
        }
    }

    private static string LoadDocumentText(LegalDocumentKind documentKind)
    {
        var path = ResolveLegalMarkdownPath(documentKind);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            return MarkdownToPlainText(File.ReadAllText(path));

        return DocumentTitle(documentKind);
    }

    private static string? ResolveLegalMarkdownPath(LegalDocumentKind documentKind)
    {
        var relativePath = Path.Combine("docs", "legal", MarkdownFileName(documentKind));
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, relativePath),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", relativePath)),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", relativePath))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string MarkdownFileName(LegalDocumentKind documentKind) =>
        documentKind == LegalDocumentKind.TermsOfService
            ? "terms-of-service.md"
            : "privacy-policy.md";

    private static string DocumentTitle(LegalDocumentKind documentKind) =>
        documentKind == LegalDocumentKind.TermsOfService
            ? "Spectralis Terms of Service"
            : "Spectralis Privacy Policy";

    private static string MarkdownToPlainText(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder(markdown.Length);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("#", StringComparison.Ordinal))
                line = line.TrimStart('#').TrimStart();
            if (line.StartsWith("- ", StringComparison.Ordinal))
                line = "  * " + line[2..];

            line = Regex.Replace(line, @"`([^`]+)`", "$1");
            line = Regex.Replace(line, @"\[(?<text>[^\]]+)\]\([^)]+\)", "${text}");
            builder.AppendLine(line);
        }

        return builder.ToString().Trim();
    }

    private static string? ResolveLegalPagePath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, LegalPageFileName),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", LegalPageFileName)),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", LegalPageFileName))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void OpenLegalPage()
    {
        var path = ResolveLegalPagePath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        OpenUrl(path);
    }

    private static void OpenUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = value,
            UseShellExecute = true
        });
    }
}
