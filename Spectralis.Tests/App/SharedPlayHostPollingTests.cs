using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class SharedPlayHostPollingTests
{
    [Fact]
    public void HostingThatResumesFromSettingsStartsListeningForRequests()
    {
        var vm = new SharedPlayViewModel();
        Assert.False(vm.IsPolling);

        // Shared Play was left on: playing a track creates the room without the Start button ever being pressed.
        vm.ApplySettings(new AppSettings { SharedPlayEnabled = true });

        Assert.True(vm.IsPolling);
    }

    [Fact]
    public void HostingThatIsOffDoesNotPoll()
    {
        var vm = new SharedPlayViewModel();

        vm.ApplySettings(new AppSettings { SharedPlayEnabled = false });

        Assert.False(vm.IsPolling);
    }
}
