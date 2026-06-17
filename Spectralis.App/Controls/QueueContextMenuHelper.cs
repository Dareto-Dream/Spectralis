using Avalonia.Controls;
using Spectralis.App.ViewModels;
using Spectralis.Core.Queue;

namespace Spectralis.App.Controls
{
    public static class QueueContextMenuHelper
    {
        public static ContextMenu Build(QueueViewModel vm, PlayQueueItem item)
        {
            var menu = new ContextMenu();

            menu.Items.Add(new MenuItem
            {
                Header = "Play Now",
                Command = vm.PlaySelectedCommand
            });

            menu.Items.Add(new MenuItem
            {
                Header = "Move Up",
                Command = vm.MoveSelectedUpCommand
            });

            menu.Items.Add(new MenuItem
            {
                Header = "Move Down",
                Command = vm.MoveSelectedDownCommand
            });

            menu.Items.Add(new Separator());

            menu.Items.Add(new MenuItem
            {
                Header = "Remove from Queue",
                Command = vm.RemoveSelectedCommand
            });

            menu.Items.Add(new Separator());

            menu.Items.Add(new MenuItem
            {
                Header = "Clear Queue",
                Command = vm.ClearCommand
            });

            return menu;
        }
    }
}
