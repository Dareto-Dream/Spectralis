namespace Spectralis;

/// <summary>
/// Standalone runner — invoke via dotnet-script or a temporary console entrypoint.
/// In normal builds this file is excluded; the smoke tests live in ObsLayoutSmokeTests.cs.
/// </summary>
internal static class RunSmokeTests
{
    public static void Execute()
    {
        try
        {
            ObsLayoutSmokeTests.RunAll();
            System.Diagnostics.Debug.WriteLine("✅ All OBS smoke tests passed.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ OBS smoke tests FAILED:\n{ex.Message}");
            throw;
        }
    }
}
