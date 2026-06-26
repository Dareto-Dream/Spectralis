namespace Spectralis;

public partial class Form1
{
    private readonly EffectChain effectChain = new();
    private EffectsChainDialog? effectsDialog;

    private void InitializeEffects()
    {
        // Wire engine to effect chain
        engine.SetEffectChain(effectChain);
        effectChain.Changed += (_, _) => engine.RebuildEffectChain();

        // ── File menu entry ──────────────────────────────────────────────────
        var mniEffects = new ToolStripMenuItem
        {
            Name = "mniEffectsChain",
            Text = "Effects Chain...",
        };
        mniEffects.Click += (_, _) => OpenEffectsChain();

        toolsToolStripMenuItem.DropDownItems.Add(mniEffects);
    }

    private void OpenEffectsChain()
    {
        if (effectsDialog is { IsDisposed: false })
        {
            effectsDialog.BringToFront();
            return;
        }

        effectsDialog = new EffectsChainDialog(effectChain, themePalette);
        effectsDialog.Show(this);
    }
}
