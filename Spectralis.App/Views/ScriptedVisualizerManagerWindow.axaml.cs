using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spectralis.Core.Visualizers.Scripting;

namespace Spectralis.App.Views;

public partial class ScriptedVisualizerManagerWindow : Window
{
    private readonly Action<ScriptedVisualizerDefinition?> _applyCallback;
    private List<ScriptedVisualizerDefinition> _scripts = [];
    private ScriptedVisualizerDefinition? _current;
    private bool _suppressEvents;

    public ScriptedVisualizerManagerWindow(Action<ScriptedVisualizerDefinition?> applyCallback)
    {
        _applyCallback = applyCallback;
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        _scripts = ScriptedVisualizerStore.LoadAll();
        RefreshList();
        if (_scripts.Count > 0)
            ScriptList.SelectedIndex = 0;
        else
            SetCurrent(null);
    }

    private void RefreshList(bool keepSelection = false)
    {
        var prev = keepSelection ? ScriptList.SelectedIndex : -1;
        _suppressEvents = true;
        ScriptList.ItemsSource = null;
        ScriptList.ItemsSource = _scripts.Select(s => s.Name).ToList();
        if (keepSelection && prev >= 0 && prev < _scripts.Count)
            ScriptList.SelectedIndex = prev;
        _suppressEvents = false;
    }

    private void SetCurrent(ScriptedVisualizerDefinition? def)
    {
        _current = def;
        _suppressEvents = true;
        NameBox.Text = def?.Name ?? string.Empty;
        ScriptEditor.Text = def?.Script ?? string.Empty;
        _suppressEvents = false;

        bool has = def is not null;
        NameBox.IsEnabled = has;
        ScriptEditor.IsEnabled = has;
        BtnDelete.IsEnabled = has;
        BtnApply.IsEnabled = has;
        BtnSave.IsEnabled = has;
        BtnExport.IsEnabled = has;
        ErrorLabel.IsVisible = false;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var idx = ScriptList.SelectedIndex;
        SetCurrent(idx >= 0 && idx < _scripts.Count ? _scripts[idx] : null);
    }

    private void OnNew(object? sender, RoutedEventArgs e)
    {
        var def = new ScriptedVisualizerDefinition();
        ScriptedVisualizerStore.Save(def);
        _scripts.Add(def);
        RefreshList();
        ScriptList.SelectedIndex = _scripts.Count - 1;
        NameBox.Focus();
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var confirm = await ConfirmWindow.ShowAsync(this,
            "Delete Script",
            $"Delete script \"{_current.Name}\"?",
            "Delete", "Cancel");
        if (!confirm) return;
        ScriptedVisualizerStore.Delete(_current.Id);
        _scripts.Remove(_current);
        RefreshList();
        SetCurrent(null);
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Script",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JavaScript") { Patterns = ["*.js"] },
                new FilePickerFileType("All files")  { Patterns = ["*.*"] },
            ],
        });
        if (files.Count == 0) return;
        var file = files[0];
        var code = await File.ReadAllTextAsync(file.Path.LocalPath);
        var name = Path.GetFileNameWithoutExtension(file.Name);
        var def = new ScriptedVisualizerDefinition { Name = name, Script = code };
        ScriptedVisualizerStore.Save(def);
        _scripts.Add(def);
        RefreshList();
        ScriptList.SelectedIndex = _scripts.Count - 1;
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Script",
            SuggestedFileName = _current.Name + ".js",
            FileTypeChoices =
            [
                new FilePickerFileType("JavaScript") { Patterns = ["*.js"] },
                new FilePickerFileType("All files")  { Patterns = ["*.*"] },
            ],
        });
        if (file is null) return;
        await File.WriteAllTextAsync(file.Path.LocalPath, _current.Script);
    }

    private void OnNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || _current is null) return;
        _current.Name = NameBox.Text ?? string.Empty;
        RefreshList(keepSelection: true);
    }

    private void OnScriptChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || _current is null) return;
        _current.Script = ScriptEditor.Text ?? string.Empty;
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        SaveCurrent();
        _applyCallback(_current);
    }

    private void OnSave(object? sender, RoutedEventArgs e) => SaveCurrent();

    private void OnClearActive(object? sender, RoutedEventArgs e) => _applyCallback(null);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void SaveCurrent()
    {
        if (_current is null) return;
        ScriptedVisualizerStore.Save(_current);
    }
}
