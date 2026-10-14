using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class SharedPlayHostPollingTests
{
    [Fact]
    public void HostingThatResumesFromSettingsStartsListeningForRequests()
    {
        using var vm = new SharedPlayViewModel();
        Assert.False(vm.IsPolling);

        // Shared Play was left on: playing a track creates the room without the Start button ever being pressed.
        // Never contact the developer's saved production account or persist its room into test settings.
        vm.ApplySettings(new AppSettings { SharedPlayEnabled = true, SharedPlayCdnBaseUrl = SharedPlayDefaults.StagingCdnBaseUrl });

        Assert.True(vm.IsPolling);
    }

    [Fact]
    public void HostingThatIsOffDoesNotPoll()
    {
        using var vm = new SharedPlayViewModel();

        vm.ApplySettings(new AppSettings { SharedPlayEnabled = false, SharedPlayCdnBaseUrl = SharedPlayDefaults.StagingCdnBaseUrl });

        Assert.False(vm.IsPolling);
    }
}
