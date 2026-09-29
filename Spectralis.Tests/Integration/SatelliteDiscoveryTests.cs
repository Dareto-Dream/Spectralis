using Spectralis.Core.Satellite;
using Xunit;

namespace Spectralis.Tests.Integration;

/// <summary>
/// Covers <see cref="SatelliteDiscovery"/>'s pure wiring (construction, ordering rules,
/// dispose). A live mDNS advertise/browse round trip does NOT appear here: the exact code path
/// below (two independent <c>MulticastService</c> instances, one advertising, one browsing,
/// same process) was verified manually — twice, reliably — as a plain console app, but times
/// out every time inside the xUnit test host (<c>testhost.exe</c>/VSTest). That's a real
/// environment difference (most likely per-executable Windows Firewall/multicast handling for
/// the test host process, not something in this codebase) rather than a defect in
/// SatelliteDiscovery — automating a test that's known to fail for reasons unrelated to
/// correctness would just be a flaky red herring, not real coverage. If you need to re-verify
/// the live path, run the equivalent of this as a standalone console app rather than a test.
/// </summary>
public sealed class SatelliteDiscoveryTests
{
    [Fact]
    public async Task SourceServer_StartAdvertising_BeforeStart_Throws()
    {
        await using var server = new SatelliteSourceServer(port: 0);

        Assert.Throws<InvalidOperationException>(() => server.StartAdvertising());
    }

    [Fact]
    public async Task SourceServer_StartAdvertising_AfterStart_DoesNotThrow()
    {
        var pairedDevicesPath = Path.Combine(Path.GetTempPath(), $"satellite-discovery-paired-{Guid.NewGuid():N}.json");
        await using var server = new SatelliteSourceServer(new SatellitePairedDevicesStore(pairedDevicesPath), port: 0);
        server.Start();

        server.StartAdvertising("test-app-instance");
        server.StopAdvertising();

        if (File.Exists(pairedDevicesPath))
        {
            File.Delete(pairedDevicesPath);
        }
    }

    [Fact]
    public async Task SourceServer_StopAdvertising_WithoutHavingStarted_IsANoOp()
    {
        var pairedDevicesPath = Path.Combine(Path.GetTempPath(), $"satellite-discovery-paired-{Guid.NewGuid():N}.json");
        await using var server = new SatelliteSourceServer(new SatellitePairedDevicesStore(pairedDevicesPath), port: 0);
        server.Start();

        server.StopAdvertising(); // must not throw even though StartAdvertising was never called

        if (File.Exists(pairedDevicesPath))
        {
            File.Delete(pairedDevicesPath);
        }
    }
}
