using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Spectralis.Core.Queue;

namespace Spectralis.App.Controls
{
    public partial class QueueItemControl : UserControl
    {
        private bool _dragging;
        private Point _dragStart;

        public event EventHandler<int>? RemoveRequested;
        public event EventHandler<(int From, int To)>? MoveRequested;

        public QueueItemControl() => InitializeComponent();

        private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

        private void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
        {
            _dragging = true;
            _dragStart = e.GetPosition(this);
            e.Handled = true;
        }

        private void OnDragHandleMoved(object? sender, PointerEventArgs e)
        {
            if (!_dragging) return;
            e.Handled = true;
        }

        private void OnDragHandleReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            e.Handled = true;
        }

        private void OnRemoveClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is PlayQueueItem item)
                RemoveRequested?.Invoke(this, -1);
        }
    }
}
