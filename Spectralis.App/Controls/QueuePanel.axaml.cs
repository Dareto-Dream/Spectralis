using System;
using Avalonia.Controls;
using Avalonia.Input;
using Spectralis.App.ViewModels;
using Spectralis.Core.Queue;

namespace Spectralis.App.Controls
{
    public partial class QueuePanel : UserControl
    {
        private int _dragFromIndex = -1;

        public QueuePanel() => InitializeComponent();

        private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

        private void OnItemRemoveRequested(object? sender, int index)
        {
            if (DataContext is not QueueViewModel vm) return;
            if (sender is QueueItemControl ctrl && ctrl.DataContext is PlayQueueItem item)
            {
                int idx = vm.Items.IndexOf(item);
                if (idx >= 0) vm.RemoveSelectedCommand.Execute(null);
            }
        }

        private void OnItemMoveRequested(object? sender, (int From, int To) args)
        {
            if (DataContext is not QueueViewModel vm) return;
            vm.DragMoveCommand.Execute(new[] { args.From, args.To });
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (DataContext is QueueViewModel vm && _dragFromIndex >= 0)
            {
                var pos = e.GetPosition(this);
                int toIndex = EstimateRowIndex(pos.Y);
                if (toIndex >= 0 && toIndex != _dragFromIndex)
                {
                    vm.DragMoveCommand.Execute(new[] { _dragFromIndex, toIndex });
                    _dragFromIndex = toIndex;
                }
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _dragFromIndex = -1;
        }

        private int EstimateRowIndex(double y)
        {
            const double rowHeight = 48.0;
            int idx = (int)(y / rowHeight);
            if (DataContext is QueueViewModel vm)
                return Math.Clamp(idx, 0, vm.Items.Count - 1);
            return -1;
        }
    }
}
