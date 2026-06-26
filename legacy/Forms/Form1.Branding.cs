using System.Diagnostics;

namespace Spectralis;

public partial class Form1
{
    private const string DeltavDevsName = "DeltaVDevs";
    private const string DeltavDevsUrl = "https://deltavdevs.com";

    public event EventHandler? ManualUpdateCheckRequested;

    private void ShowAboutDeltavDevs()
    {
        using var dialog = new AboutSpectralisDialog(
            themePalette,
            DeltavDevsName,
            DeltavDevsUrl);

        dialog.ShowDialog(this);
    }

    private void ShowLegalDocument(LegalDocumentKind documentKind)
    {
        using var dialog = new LegalDocumentDialog(themePalette, documentKind);
        dialog.ShowDialog(this);
    }

    private void ShowRedeemVisualizerDialog()
    {
        var previousChoice = GetCurrentVisualizerChoice();
        using var dialog = new RedeemVisualizerDialog(themePalette, redeemableVisualizers);
        dialog.ShowDialog(this);
        if (!dialog.InstalledVisualizersChanged)
            return;

        redeemableVisualizers.Reload();
        var preferredChoice = string.IsNullOrWhiteSpace(dialog.RedeemedVisualizerId)
            ? previousChoice
            : VisualizerChoice.Installed(dialog.RedeemedVisualizerId);
        RefreshVisualizerModeOptions(preferredChoice);
        UpdateUiState();
    }

    private void ClearRedeemedVisualizers()
    {
        if (redeemableVisualizers.Installed.Count == 0)
        {
            MessageBox.Show(
                this,
                "There are no redeemed visualizers to clear.",
                "Redeemed Visualizers",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            this,
            "Clear all redeemed visualizers from this device?",
            "Clear Redeemed Visualizers",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
            return;

        var preferredChoice = GetCurrentVisualizerChoice();
        try
        {
            redeemableVisualizers.ClearAll();
            if (appSettings.DefaultVisualizerKey.StartsWith(
                VisualizerChoice.InstalledPrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                appSettings.DefaultVisualizerKey = VisualizerChoice.BuiltIn(appSettings.DefaultVisualizer).Key;
                SaveAppSettings();
            }

            RefreshVisualizerModeOptions(preferredChoice);
            UpdateUiState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Spectralis could not clear redeemed visualizers.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Clear Redeemed Visualizers",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void OpenDeltavDevsSite()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = DeltavDevsUrl,
            UseShellExecute = true
        });
    }

    private void helpRedeemVisualizerToolStripMenuItem_Click(object sender, EventArgs e) =>
        ShowRedeemVisualizerDialog();

    private void helpClearRedeemedVisualizersToolStripMenuItem_Click(object sender, EventArgs e) =>
        ClearRedeemedVisualizers();

    private void helpClearCachedAlbumStateToolStripMenuItem_Click(object sender, EventArgs e) =>
        ClearCachedAlbumState();

    private void helpCheckForUpdatesToolStripMenuItem_Click(object sender, EventArgs e) =>
        ManualUpdateCheckRequested?.Invoke(this, EventArgs.Empty);

    private void helpTermsOfServiceToolStripMenuItem_Click(object sender, EventArgs e) =>
        ShowLegalDocument(LegalDocumentKind.TermsOfService);

    private void helpPrivacyPolicyToolStripMenuItem_Click(object sender, EventArgs e) =>
        ShowLegalDocument(LegalDocumentKind.PrivacyPolicy);
}
