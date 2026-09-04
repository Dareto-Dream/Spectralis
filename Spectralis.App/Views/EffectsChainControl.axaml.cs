using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class EffectsChainControl : UserControl
{
    public EffectsChainControl()
    {
        InitializeComponent();
    }

    private EffectsChainViewModel? ViewModel => DataContext as EffectsChainViewModel;

    private EqEditorViewModel? EqEditor => ViewModel?.SelectedEffect?.EqEditor;

    private void OnAddEffect(object? sender, RoutedEventArgs e) => ViewModel?.AddSelectedEffect();

    private void OnEqReset(object? sender, RoutedEventArgs e) => EqEditor?.ResetToFlat();

    private void OnEqSavePreset(object? sender, RoutedEventArgs e)
    {
        if (EqEditor is { } editor)
        {
            editor.SaveCurrentAsPreset(editor.NewPresetName);
        }
    }

    private void OnEqDeletePreset(object? sender, RoutedEventArgs e) => EqEditor?.DeleteSelectedPreset();

    private void OnMoveUp(object? sender, RoutedEventArgs e) => ViewModel?.MoveSelectedEffectUp();

    private void OnMoveDown(object? sender, RoutedEventArgs e) => ViewModel?.MoveSelectedEffectDown();

    private void OnRemove(object? sender, RoutedEventArgs e) => ViewModel?.RemoveSelectedEffect();
}
