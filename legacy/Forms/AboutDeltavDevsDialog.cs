using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Spectralis;

internal sealed class AboutSpectralisDialog : Form
{
    private readonly ThemePalette palette;
    private readonly string companyName;
    private readonly string companyUrl;
    private readonly ModernButton btnCopyVersion;
    private readonly ModernButton btnVisit;
    private readonly ModernButton btnClose;

    public AboutSpectralisDialog(ThemePalette palette, string companyName, string companyUrl)
    {
        this.palette = palette;
        this.companyName = companyName;
        this.companyUrl = companyUrl;

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(580, 520);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(500, 420);
        Padding = new Padding(22);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "About Spectralis";

        btnCopyVersion = new ModernButton
        {
            Size = new Size(132, 38),
            Text = "Copy Version"
        };
        btnCopyVersion.Click += (_, _) => CopyVersionInfo();

        btnVisit = new ModernButton
        {
            Size = new Size(154, 38),
            Text = "Visit DeltaVDevs"
        };
        btnVisit.Click += (_, _) => OpenCompanySite();

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

        var header = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 16),
            Padding = Padding.Empty,
            RowCount = 2
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = Padding.Empty,
            Text = "Spectralis"
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9.25F),
            Margin = new Padding(0, 8, 0, 0),
            Text = "Audio playback, visualizers, shared listening, and creator capsules."
        };

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);

        var scrollHost = new Panel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 10, 0)
        };
        scrollHost.Controls.Add(CreateDetails());

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
        btnVisit.Margin = new Padding(0, 0, 10, 0);
        btnCopyVersion.Margin = new Padding(0, 0, 10, 0);
        footer.Controls.Add(btnClose);
        footer.Controls.Add(btnVisit);
        footer.Controls.Add(btnCopyVersion);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(scrollHost, 0, 1);
        root.Controls.Add(footer, 0, 2);
        return root;
    }

    private Control CreateDetails()
    {
        var details = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 1
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        AddSection(details, "App", new[]
        {
            ("Version", GetCurrentAppVersion()),
            ("Update channel", "Stable"),
            ("Update behavior", "Installs in app, then applies after every Spectralis window is closed and reopened."),
            ("Executable", Environment.ProcessPath ?? Application.ExecutablePath),
            ("Install folder", GetInstallFolder())
        });

        AddSection(details, "System", new[]
        {
            ("Runtime", RuntimeInformation.FrameworkDescription),
            ("OS", RuntimeInformation.OSDescription),
            ("Architecture", RuntimeInformation.ProcessArchitecture.ToString())
        });

        AddSection(details, "Creator", new[]
        {
            ("Made by", companyName),
            ("Website", companyUrl),
            ("Support", "Use the latest installer if an automatic update cannot finish.")
        });

        return details;
    }

    private static void AddSection(
        TableLayoutPanel parent,
        string title,
        IEnumerable<(string Name, string Value)> rows)
    {
        var section = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 18),
            Padding = Padding.Empty
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var heading = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 8),
            Text = title
        };
        section.Controls.Add(heading, 0, 0);
        section.SetColumnSpan(heading, 2);

        var rowIndex = 1;
        foreach (var (name, value) in rows)
        {
            var nameLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Margin = new Padding(0, 0, 12, 8),
                Text = name
            };

            var valueLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Margin = new Padding(0, 0, 0, 8),
                MaximumSize = new Size(360, 0),
                Text = string.IsNullOrWhiteSpace(value) ? "Unknown" : value
            };

            section.Controls.Add(nameLabel, 0, rowIndex);
            section.Controls.Add(valueLabel, 1, rowIndex);
            rowIndex++;
        }

        parent.Controls.Add(section, 0, parent.RowCount - 1);
        parent.RowCount++;
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
                label.ForeColor = label.Font.Bold ? palette.TextPrimaryColor : palette.TextSecondaryColor;
        }

        ThemeControlStyler.ApplyGhostButtonTheme(btnCopyVersion, palette, palette.AccentSoftColor);
        ThemeControlStyler.ApplyGhostButtonTheme(btnVisit, palette, palette.AccentSoftColor);
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

    private static string GetCurrentAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AboutSpectralisDialog).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Split('+')[0].Trim();

        return assembly.GetName().Version?.ToString() ?? "";
    }

    private static string GetInstallFolder()
    {
        var processPath = Environment.ProcessPath ?? Application.ExecutablePath;
        return string.IsNullOrWhiteSpace(processPath)
            ? ""
            : Path.GetDirectoryName(processPath) ?? "";
    }

    private static void CopyVersionInfo()
    {
        var version = GetCurrentAppVersion();
        Clipboard.SetText(string.IsNullOrWhiteSpace(version) ? "Spectralis" : $"Spectralis {version}");
    }

    private void OpenCompanySite()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = companyUrl,
            UseShellExecute = true
        });
    }
}
