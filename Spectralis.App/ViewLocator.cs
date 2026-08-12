using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Spectralis.App.ViewModels;

namespace Spectralis.App;

/// <summary>
/// Maps a ViewModel to its View by naming convention:
/// Spectralis.App.ViewModels.FooViewModel -> Spectralis.App.Views.FooView.
///
/// Views are cached per ViewModel instance (tied to the ViewModel's lifetime via
/// ConditionalWeakTable, so nothing leaks) so switching the sidebar back and forth
/// reuses the same control tree instead of rebuilding it — sections used to fully
/// re-construct on every visit, which is what made section switching feel slow.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private static readonly ConditionalWeakTable<object, Control> Cache = new();

    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        if (Cache.TryGetValue(data, out var cached))
        {
            return cached;
        }

        var name = data.GetType().FullName!
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type is null)
        {
            return new TextBlock { Text = "Missing view: " + name };
        }

        var view = (Control)Activator.CreateInstance(type)!;
        // Ensure the view fills the ContentControl rather than sizing to content.
        view.HorizontalAlignment = HorizontalAlignment.Stretch;
        view.VerticalAlignment = VerticalAlignment.Stretch;
        Cache.Add(data, view);
        return view;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
