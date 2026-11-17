using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.App.Views;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.App;

[Collection("Visualizer UI")]
public sealed class AppMotionTests
{
    [AvaloniaFact]
    public void Disabling_interface_motion_clears_durations_and_restores_content_opacity()
    {
        var expander = new Expander { Header = "Options", Content = new TextBlock { Text = "Content" } };
        var content = new Border { Child = expander };
        var window = new Window { Content = content };
        try
        {
            window.Show();
            AppMotionService.Apply(true);
            Assert.NotNull(expander.ContentTransition);
            AppMotionService.Reveal(content);
            AppMotionService.Apply(false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(AppMotionService.Enabled);
            Assert.Equal(TimeSpan.Zero, Application.Current!.Resources["Duration.Base"]);
            Assert.Null(Application.Current.Resources["Motion.Expansion"]);
            Assert.Equal(1, content.Opacity);
            Assert.Null(expander.ContentTransition);
        }
        finally { window.Close(); AppMotionService.Apply(true); }
    }
}
