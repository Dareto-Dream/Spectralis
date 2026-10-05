using ReactiveUI;
using Spectralis.App.Services;

namespace Spectralis.App.ViewModels;

public sealed record WidgetGroup(string Title, IReadOnlyList<DevWidget> Widgets);

/// <summary>Backs the Widget Tester tab: a filterable catalog of everything that can be opened, and what the last spawn did.</summary>
public sealed class WidgetTesterViewModel : ViewModelBase
{
    private string _filterText = string.Empty;
    private IReadOnlyList<WidgetGroup> _groups;
    private string _lastResult = "Pick a widget and press Open.";

    public WidgetTesterViewModel() => _groups = BuildGroups(string.Empty);

    public string FilterText
    {
        get => _filterText;
        set
        {
            this.RaiseAndSetIfChanged(ref _filterText, value);
            Groups = BuildGroups(value);
        }
    }

    public IReadOnlyList<WidgetGroup> Groups
    {
        get => _groups;
        private set => this.RaiseAndSetIfChanged(ref _groups, value);
    }

    public string LastResult
    {
        get => _lastResult;
        private set => this.RaiseAndSetIfChanged(ref _lastResult, value);
    }

    /// <summary>Opens the widget over <paramref name="owner"/> and records what it returned. A widget that throws shows the error instead of taking the tools window down.</summary>
    public async Task SpawnAsync(DevWidget widget, Avalonia.Controls.Window owner)
    {
        LastResult = $"{widget.Name}: opening...";
        try
        {
            LastResult = $"{widget.Name}: {await widget.Spawn(owner)}";
        }
        catch (Exception ex)
        {
            LastResult = $"{widget.Name} failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    internal static IReadOnlyList<WidgetGroup> BuildGroups(string filter, IEnumerable<DevWidget>? source = null)
    {
        var words = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (source ?? DevWidgetCatalog.All)
            .Where(w => words.All(word =>
                w.Name.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                w.Description.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                w.Category.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(w => w.Category)
            .Select(g => new WidgetGroup(g.Key, g.ToList()))
            .ToList();
    }
}
