using System.Drawing;
using System.IO;

namespace Spectralis;

internal sealed class ScriptVisualizerManagerDialog : Form
{
    private readonly ThemePalette _theme;
    private readonly Action<ScriptedVisualizerDefinition?> _applyCallback;

    private readonly ListBox _lstScripts;
    private readonly ScriptEditorControl _editor;
    private readonly Button _btnNew;
    private readonly Button _btnDelete;
    private readonly Button _btnApply;
    private readonly Button _btnClose;
    private readonly Button _btnImport;
    private readonly Button _btnExport;
    private readonly TextBox _txtName;

    private List<ScriptedVisualizerDefinition> _scripts = new();
    private ScriptedVisualizerDefinition? _current;

    public ScriptVisualizerManagerDialog(ThemePalette theme, Action<ScriptedVisualizerDefinition?> applyCallback)
    {
        _theme = theme;
        _applyCallback = applyCallback;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        Text                = "Scripted Visualizers";
        FormBorderStyle     = FormBorderStyle.SizableToolWindow;
        StartPosition       = FormStartPosition.CenterParent;
        ShowInTaskbar       = false;
        ClientSize          = new Size(840, 520);
        MinimumSize         = new Size(700, 400);

        // ── Left panel: script list ──────────────────────────────────────────
        var leftPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 220,
            Padding = new Padding(6),
        };

        _lstScripts = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f),
            BorderStyle = BorderStyle.FixedSingle,
        };
        _lstScripts.SelectedIndexChanged += LstScripts_SelectedIndexChanged;

        var btnBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Padding = new Padding(2, 2, 2, 0),
            WrapContents = false,
        };

        _btnNew    = MakeBtn("New",      50);
        _btnDelete = MakeBtn("Delete",   60);
        _btnImport = MakeBtn("Import…",  70);
        _btnExport = MakeBtn("Export…",  70);

        _btnNew.Click    += BtnNew_Click;
        _btnDelete.Click += BtnDelete_Click;
        _btnImport.Click += BtnImport_Click;
        _btnExport.Click += BtnExport_Click;

        btnBar.Controls.AddRange([_btnNew, _btnDelete, _btnImport, _btnExport]);
        leftPanel.Controls.Add(_lstScripts);
        leftPanel.Controls.Add(btnBar);

        // ── Right panel: editor ───────────────────────────────────────────────
        var rightPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 6, 6, 0),
        };

        var nameBar = new Panel { Dock = DockStyle.Top, Height = 32 };
        var lblName = new Label
        {
            Text = "Name:",
            AutoSize = true,
            Location = new Point(0, 8),
            Font = new Font("Segoe UI", 9.5f),
        };
        _txtName = new TextBox
        {
            Location = new Point(48, 4),
            Width = 240,
            Font = new Font("Segoe UI", 9.5f),
        };
        _txtName.TextChanged += (_, _) =>
        {
            if (_current is null) return;
            _current.Name = _txtName.Text;
            RefreshList(keepSelection: true);
        };
        nameBar.Controls.Add(lblName);
        nameBar.Controls.Add(_txtName);

        _editor = new ScriptEditorControl { Dock = DockStyle.Fill };
        _editor.ScriptChanged += (_, _) =>
        {
            if (_current is null) return;
            _current.Script = _editor.Script;
        };

        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        _btnApply = new Button
        {
            Text = "Apply to Visualizer",
            Width = 140,
            Dock = DockStyle.Left,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
        };
        _btnApply.Click += BtnApply_Click;

        var btnSave = new Button
        {
            Text = "Save",
            Width = 70,
            Dock = DockStyle.Left,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
        };
        btnSave.Click += (_, _) => SaveCurrent();

        _btnClose = new Button
        {
            Text = "Close",
            Width = 70,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
        };
        _btnClose.Click += (_, _) => Close();

        bottomBar.Controls.Add(_btnApply);
        bottomBar.Controls.Add(btnSave);
        bottomBar.Controls.Add(_btnClose);

        rightPanel.Controls.Add(_editor);
        rightPanel.Controls.Add(nameBar);
        rightPanel.Controls.Add(bottomBar);

        Controls.Add(rightPanel);
        Controls.Add(leftPanel);

        ApplyTheme();
        Reload();
        UpdateEnabled();
    }

    private void Reload()
    {
        _scripts = ScriptedVisualizerStore.LoadAll();
        RefreshList();
        if (_scripts.Count > 0)
            _lstScripts.SelectedIndex = 0;
    }

    private void RefreshList(bool keepSelection = false)
    {
        var prevIdx = keepSelection ? _lstScripts.SelectedIndex : -1;
        _lstScripts.Items.Clear();
        foreach (var def in _scripts)
            _lstScripts.Items.Add(def.Name);
        if (keepSelection && prevIdx >= 0 && prevIdx < _lstScripts.Items.Count)
            _lstScripts.SelectedIndex = prevIdx;
    }

    private void LstScripts_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var idx = _lstScripts.SelectedIndex;
        if (idx < 0 || idx >= _scripts.Count)
        {
            _current = null;
            _editor.Script = "";
            _txtName.Text = "";
        }
        else
        {
            _current = _scripts[idx];
            _editor.Script = _current.Script;
            _txtName.Text = _current.Name;
        }
        UpdateEnabled();
    }

    private void BtnNew_Click(object? sender, EventArgs e)
    {
        var def = new ScriptedVisualizerDefinition();
        ScriptedVisualizerStore.Save(def);
        _scripts.Add(def);
        _lstScripts.Items.Add(def.Name);
        _lstScripts.SelectedIndex = _lstScripts.Items.Count - 1;
        _txtName.Focus();
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (_current is null) return;
        var confirm = MessageBox.Show(this,
            $"Delete script \"{_current.Name}\"?",
            "Delete Script",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;
        ScriptedVisualizerStore.Delete(_current.Id);
        _scripts.Remove(_current);
        RefreshList();
        _current = null;
        UpdateEnabled();
    }

    private void BtnApply_Click(object? sender, EventArgs e)
    {
        if (_current is null) return;
        SaveCurrent();
        _applyCallback(_current);
    }

    private void BtnImport_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Import Script",
            Filter = "JavaScript|*.js|All files|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var code = File.ReadAllText(dlg.FileName);
        var name = Path.GetFileNameWithoutExtension(dlg.FileName);
        var def = new ScriptedVisualizerDefinition { Name = name, Script = code };
        ScriptedVisualizerStore.Save(def);
        _scripts.Add(def);
        _lstScripts.Items.Add(def.Name);
        _lstScripts.SelectedIndex = _lstScripts.Items.Count - 1;
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (_current is null) return;
        using var dlg = new SaveFileDialog
        {
            Title = "Export Script",
            FileName = _current.Name + ".js",
            Filter = "JavaScript|*.js|All files|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dlg.FileName, _current.Script);
    }

    private void SaveCurrent()
    {
        if (_current is null) return;
        ScriptedVisualizerStore.Save(_current);
    }

    private void UpdateEnabled()
    {
        bool has = _current is not null;
        _btnDelete.Enabled = has;
        _btnApply.Enabled = has;
        _btnExport.Enabled = has;
        _txtName.Enabled = has;
        _editor.Enabled = has;
    }

    private static Button MakeBtn(string text, int width)
    {
        return new Button
        {
            Text      = text,
            Width     = width,
            Height    = 28,
            Margin    = new Padding(0, 0, 2, 0),
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 8.5f),
        };
    }

    private void ApplyTheme()
    {
        BackColor = _theme.WindowBackColor;
        ForeColor = _theme.TextPrimaryColor;
        _lstScripts.BackColor = _theme.SurfaceAltBackColor;
        _lstScripts.ForeColor = _theme.TextPrimaryColor;
        _txtName.BackColor = _theme.SurfaceAltBackColor;
        _txtName.ForeColor = _theme.TextPrimaryColor;
        _editor.ApplyTheme(_theme);

        foreach (var btn in new[] { _btnNew, _btnDelete, _btnImport, _btnExport, _btnApply, _btnClose })
        {
            btn.BackColor = _theme.SurfaceRaisedColor;
            btn.ForeColor = _theme.TextSecondaryColor;
            btn.FlatAppearance.BorderColor = _theme.BorderStrongColor;
        }

        _btnApply.BackColor = _theme.AccentPrimaryColor;
        _btnApply.ForeColor = _theme.AccentContrastColor;
    }
}
