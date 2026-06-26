namespace Spectralis;

public partial class Form1
{
    private ScriptVisualizerManagerDialog? scriptVisualizerDialog;

    private void InitializeScriptedVisualizers()
    {
        var mniScripts = new ToolStripMenuItem
        {
            Name = "mniScriptedVisualizers",
            Text = "Scripted Visualizers...",
        };
        mniScripts.Click += (_, _) => OpenScriptVisualizerManager();

        toolsToolStripMenuItem.DropDownItems.Add(mniScripts);
    }

    private void OpenScriptVisualizerManager()
    {
        if (scriptVisualizerDialog is { IsDisposed: false })
        {
            scriptVisualizerDialog.BringToFront();
            return;
        }

        scriptVisualizerDialog = new ScriptVisualizerManagerDialog(
            themePalette,
            def => ApplyScriptedVisualizer(def));
        scriptVisualizerDialog.Show(this);
    }

    internal void ApplyScriptedVisualizer(ScriptedVisualizerDefinition? def)
    {
        if (def is null)
        {
            visualizerControl.ScriptedRenderer = null;
            return;
        }

        visualizerControl.ScriptedRenderer = new ScriptVisualizerRenderer(def);

        // Persist the choice as the current visualizer key
        appSettings.DefaultVisualizerKey = VisualizerChoice.Scripted(def.Id).Key;
        AppSettingsStore.Save(appSettings);
    }

}
