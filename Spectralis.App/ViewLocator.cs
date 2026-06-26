using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Spectralis.App.ViewModels;

namespace Spectralis.App;

/// <summary>
/// Maps a ViewModel to its View by naming convention:
/// Spectralis.App.ViewModels.FooViewModel -> Spectralis.App.Views.FooView.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
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
        return view;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
