namespace Spectralis;

public partial class Form1
{
    private KaraokeModeForm? karaokeForm;
    private VocalBlendEffect? karaokeVocalEffect;

    private void InitializeKaraoke()
    {
        var mniKaraoke = new ToolStripMenuItem
        {
            Name = "mniKaraokeMode",
            Text = "Karaoke Mode...",
        };
        mniKaraoke.Click += (_, _) => OpenKaraokeMode();

        toolsToolStripMenuItem.DropDownItems.Add(mniKaraoke);
    }

    private void OpenKaraokeMode()
    {
        if (karaokeForm is { IsDisposed: false })
        {
            karaokeForm.BringToFront();
            return;
        }

        // Ensure a VocalBlendEffect is in the chain (added with blend=0 so no audible change yet)
        karaokeVocalEffect = effectChain.Effects.OfType<VocalBlendEffect>().FirstOrDefault();
        if (karaokeVocalEffect is null)
        {
            karaokeVocalEffect = new VocalBlendEffect();
            effectChain.Add(karaokeVocalEffect);
            if (!effectChain.Enabled) effectChain.Enabled = true;
            engine.RebuildEffectChain();
        }

        karaokeForm = new KaraokeModeForm(
            togglePlayback: () =>
            {
                if (engine.IsLoaded)
                {
                    engine.Toggle();
                    UpdateUiState();
                }
            },
            onVocalBlend: blend =>
            {
                if (karaokeVocalEffect is not null)
                {
                    karaokeVocalEffect.Parameters.Set("blend", blend);
                    engine.RebuildEffectChain();
                }
            });

        karaokeForm.Display.SetDocument(engine.CurrentTrack?.Lyrics);
        if (engine.IsLoaded)
            karaokeForm.Display.SetPosition(engine.GetPosition());

        karaokeForm.FormClosed += (_, _) =>
        {
            // Reset vocal blend on close so playback is unaffected
            if (karaokeVocalEffect is not null)
            {
                karaokeVocalEffect.Parameters.Set("blend", 0f);
                engine.RebuildEffectChain();
                karaokeVocalEffect = null;
            }
            karaokeForm = null;
        };

        karaokeForm.Show(this);
    }

    private void TickKaraoke()
    {
        if (karaokeForm is null or { IsDisposed: true }) return;
        if (!engine.IsLoaded) return;
        karaokeForm.UpdateTime(engine.GetPosition());
    }

    partial void OnKaraokeTrackLoaded(string path)
    {
        if (karaokeForm is null or { IsDisposed: true }) return;
        karaokeForm.Display.SetDocument(engine.CurrentTrack?.Lyrics);
    }
}
