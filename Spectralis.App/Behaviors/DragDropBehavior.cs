using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;

namespace Spectralis.App.Behaviors
{
    public class DragDropBehavior : Behavior<ItemsControl>
    {
        public static readonly StyledProperty<Action<int, int>?> MoveItemProperty =
            AvaloniaProperty.Register<DragDropBehavior, Action<int, int>?>(nameof(MoveItem));

        public Action<int, int>? MoveItem
        {
            get => GetValue(MoveItemProperty);
            set => SetValue(MoveItemProperty, value);
        }

        private int _dragFrom = -1;
        private Point _dragStartPos;
        private const double MinDragDistance = 8.0;
        private bool _isDragging;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject == null) return;
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject == null) return;
            AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            AssociatedObject.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            AssociatedObject.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var pos = e.GetPosition(AssociatedObject);
            _dragFrom = HitTestItemIndex(pos);
            _dragStartPos = pos;
            _isDragging = false;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_dragFrom < 0) return;
            var pos = e.GetPosition(AssociatedObject);
            double dist = Math.Abs(pos.Y - _dragStartPos.Y);

            if (!_isDragging && dist < MinDragDistance) return;
            _isDragging = true;

            int to = HitTestItemIndex(pos);
            if (to >= 0 && to != _dragFrom)
            {
                MoveItem?.Invoke(_dragFrom, to);
                _dragFrom = to;
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _dragFrom = -1;
            _isDragging = false;
        }

        private int HitTestItemIndex(Point pos)
        {
            if (AssociatedObject == null) return -1;
            var hit = AssociatedObject.InputHitTest(pos);
            var el = hit as Control;
            while (el != null && el != AssociatedObject)
            {
                if (el.DataContext != null)
                {
                    int idx = AssociatedObject.Items.Count;
                    for (int i = 0; i < AssociatedObject.Items.Count; i++)
                        if (ReferenceEquals(AssociatedObject.Items[i], el.DataContext)) return i;
                }
                el = el.Parent as Control;
            }
            return -1;
        }
    }
}
