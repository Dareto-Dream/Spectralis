namespace Spectralis;

internal sealed class RedeemableVisualizerService : IDisposable
{
    private readonly InstalledVisualizerStore store = new();
    private readonly RedeemableVisualizerClient client = new();
    private IReadOnlyList<InstalledVisualizerDefinition> installed = Array.Empty<InstalledVisualizerDefinition>();

    public IReadOnlyList<InstalledVisualizerDefinition> Installed => installed;

    public void Reload()
    {
        installed = store.LoadAll();
    }

    public bool TryGetInstalled(string id, out InstalledVisualizerDefinition definition)
    {
        definition = installed.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))!;
        return definition is not null;
    }

    public async Task<InstalledVisualizerDefinition> RedeemAsync(
        string redeemKey,
        CancellationToken cancellationToken)
    {
        var package = await client.RedeemAsync(redeemKey, cancellationToken);
        var definition = store.Install(package);
        Reload();
        return definition;
    }

    public void ClearAll()
    {
        store.ClearAll();
        Reload();
    }

    public void Dispose()
    {
        client.Dispose();
    }
}
