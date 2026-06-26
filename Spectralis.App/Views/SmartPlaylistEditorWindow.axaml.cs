using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.Core.Playlists;

namespace Spectralis.App.Views;

/// <summary>One editable smart rule row.</summary>
public sealed class SmartRuleRow
{
    public IReadOnlyList<SmartRuleField> FieldOptions { get; } = Enum.GetValues<SmartRuleField>();
    public IReadOnlyList<SmartRuleOp> OpOptions { get; } = Enum.GetValues<SmartRuleOp>();

    public SmartRuleField Field { get; set; } = SmartRuleField.Title;
    public SmartRuleOp Op { get; set; } = SmartRuleOp.Contains;
    public string Value { get; set; } = "";

    public SmartRule ToRule() => new() { Field = Field, Op = Op, Value = Value };

    public static SmartRuleRow From(SmartRule rule) => new()
    {
        Field = rule.Field,
        Op = rule.Op,
        Value = rule.Value,
    };
}

public partial class SmartPlaylistEditorWindow : Window
{
    private readonly ObservableCollection<SmartRuleRow> _rules = new();
    private SmartPlaylist? _playlist;

    public bool Saved { get; private set; }

    public SmartPlaylistEditorWindow()
    {
        InitializeComponent();
        RulesList.ItemsSource = _rules;
        MatchBox.ItemsSource = Enum.GetValues<SmartMatchMode>();
        SortBox.ItemsSource = Enum.GetValues<SmartSortField>();
    }

    public static async Task<bool> EditAsync(Window owner, SmartPlaylist playlist)
    {
        var window = new SmartPlaylistEditorWindow { _playlist = playlist };
        window.NameBox.Text = playlist.Name;
        window.MatchBox.SelectedItem = playlist.Match;
        window.SortBox.SelectedItem = playlist.SortBy;
        window.DescendingBox.IsChecked = playlist.SortDescending;
        window.LimitBox.Value = playlist.Limit;
        foreach (var rule in playlist.Rules)
        {
            window._rules.Add(SmartRuleRow.From(rule));
        }

        await window.ShowDialog(owner);
        return window.Saved;
    }

    private void OnAddRule(object? sender, RoutedEventArgs e) => _rules.Add(new SmartRuleRow());

    private void OnRemoveRule(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SmartRuleRow row })
        {
            _rules.Remove(row);
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_playlist is null)
        {
            Close();
            return;
        }

        var name = NameBox.Text?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            _playlist.Name = name;
        }

        _playlist.Match = MatchBox.SelectedItem is SmartMatchMode match ? match : SmartMatchMode.All;
        _playlist.SortBy = SortBox.SelectedItem is SmartSortField sort ? sort : SmartSortField.DateAdded;
        _playlist.SortDescending = DescendingBox.IsChecked ?? true;
        _playlist.Limit = (int)(LimitBox.Value ?? 0);
        _playlist.Rules = _rules.Select(row => row.ToRule()).ToList();
        Saved = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
