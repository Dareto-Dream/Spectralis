using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class EffectsChainWindow : Window
{
    public EffectsChainWindow()
    {
        InitializeComponent();
    }

    private EffectsChainViewModel? ViewModel => DataContext as EffectsChainViewModel;

    private void OnAddEffect(object? sender, RoutedEventArgs e) => ViewModel?.AddSelectedEffect();

    private void OnMoveUp(object? sender, RoutedEventArgs e) => ViewModel?.MoveSelectedEffectUp();

    private void OnMoveDown(object? sender, RoutedEventArgs e) => ViewModel?.MoveSelectedEffectDown();

    private void OnRemove(object? sender, RoutedEventArgs e) => ViewModel?.RemoveSelectedEffect();
}
