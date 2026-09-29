using Makaretu.Dns;

namespace Spectralis.Core.Satellite;

public sealed class SatelliteDiscoveredSource
{
    public required string InstanceName { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
}

/// <summary>
/// LAN discovery via mDNS/DNS-SD (Bonjour-style), advertising/browsing
/// <c>_spectralis-satellite._tcp</c>. Two independent capabilities in one class since a
/// desktop dev/test build plausibly wants both (advertise from the source app; browse from the
/// reference receiver) — call only the side you need.
/// </summary>
public sealed class SatelliteDiscovery : IDisposable
{
    public const string ServiceType = "_spectralis-satellite._tcp";

    private readonly MulticastService _mdns = new();
    private readonly ServiceDiscovery _serviceDiscovery;
    private ServiceProfile? _advertisedProfile;
    private bool _started;

    public event EventHandler<SatelliteDiscoveredSource>? SourceDiscovered;

    public SatelliteDiscovery()
    {
        _serviceDiscovery = new ServiceDiscovery(_mdns);
        _serviceDiscovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
    }

    /// <summary>Advertises this machine as a Satellite source. <paramref name="instanceName"/>
    /// should be something recognizable on a LAN device list (e.g. the PC's hostname).</summary>
    public void StartAdvertising(string instanceName, int port)
    {
        _advertisedProfile = new ServiceProfile(instanceName, ServiceType, (ushort)port);
        _serviceDiscovery.Advertise(_advertisedProfile);
        EnsureStarted();
    }

    public void StopAdvertising()
    {
        if (_advertisedProfile is not null)
        {
            _serviceDiscovery.Unadvertise(_advertisedProfile);
            _advertisedProfile = null;
        }
    }

    /// <summary>Starts listening for Satellite sources on the LAN — <see cref="SourceDiscovered"/>
    /// fires (possibly repeatedly for the same source, on every response) as they answer.</summary>
    public void StartBrowsing()
    {
        _mdns.NetworkInterfaceDiscovered += OnNetworkInterfaceDiscovered;
        EnsureStarted();
        _serviceDiscovery.QueryServiceInstances(ServiceType);
    }

    private void OnNetworkInterfaceDiscovered(object? sender, NetworkInterfaceEventArgs e) =>
        _serviceDiscovery.QueryServiceInstances(ServiceType);

    private void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
    {
        // Prefer an SRV record's target+port when present (the standards-correct source of
        // truth); RemoteEndPoint is a pragmatic fallback that's right often enough on a simple
        // single-NIC LAN, which is what this feature targets.
        var srv = e.Message.Answers.OfType<SRVRecord>().FirstOrDefault();
        var host = srv?.Target?.ToString() ?? e.RemoteEndPoint?.Address.ToString();
        var port = srv?.Port ?? (ushort)(e.RemoteEndPoint?.Port ?? 0);

        if (string.IsNullOrEmpty(host) || port == 0)
        {
            return;
        }

        SourceDiscovered?.Invoke(this, new SatelliteDiscoveredSource
        {
            InstanceName = e.ServiceInstanceName.ToString(),
            Host = host,
            Port = port,
        });
    }

    private void EnsureStarted()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _mdns.Start();
    }

    public void Dispose()
    {
        StopAdvertising();
        _mdns.NetworkInterfaceDiscovered -= OnNetworkInterfaceDiscovered;
        _serviceDiscovery.ServiceInstanceDiscovered -= OnServiceInstanceDiscovered;
        if (_started)
        {
            _mdns.Stop();
        }

        _serviceDiscovery.Dispose();
        _mdns.Dispose();
    }
}
