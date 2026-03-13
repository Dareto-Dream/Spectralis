using Avalonia.Controls;
using Spectralis.App.ViewModels;
using Spectralis.App.Views;

namespace Spectralis.App.Services
{
    public static class LyricsMenuHelper
    {
        public static MenuItem BuildTimingStudioItem(TimingStudioViewModel vm)
        {
            var item = new MenuItem { Header = "Timing Studio…" };
            item.Click += (_, _) =>
            {
                var win = new TimingStudioView(vm);
                win.Show();
            };
            return item;
        }
    }
}
