using System.IO;

namespace Spectralis;

public partial class Form1
{
    private ToolStripMenuItem? ctxQueueEditTagsItem;

    private void InitializeTagEditor()
    {
        ctxQueueEditTagsItem = new ToolStripMenuItem
        {
            Name = "ctxQueueEditTags",
            Text = "Edit Tags...",
        };
        ctxQueueEditTagsItem.Click += (_, _) => OpenTagEditorForQueueItem();

        var twIndex = ctxQueue.Items.IndexOf(ctxQueueEditTw);
        ctxQueue.Items.Insert(twIndex + 1, ctxQueueEditTagsItem);
    }

    private void OpenTagEditorForQueueItem()
    {
        if (ctxQueueTargetIndex < 0) return;
        var path = queue.Items[ctxQueueTargetIndex];
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        OpenTagEditor([path]);
    }

    internal void OpenTagEditor(string[] paths)
    {
        if (paths.Length == 0) return;

        var saved = false;
        if (paths.Length == 1)
        {
            using var dlg = new TagEditorDialog(paths[0], themePalette);
            saved = dlg.ShowDialog(this) == DialogResult.OK;
        }
        else
        {
            using var dlg = new BatchTagEditorDialog(paths, themePalette);
            saved = dlg.ShowDialog(this) == DialogResult.OK;
        }

        if (saved)
            AfterTagsSaved(paths);
    }

    private void AfterTagsSaved(string[] paths)
    {
        foreach (var path in paths)
            IndexSingleFile(path);

        var currentPath = engine.CurrentTrack?.FilePath;
        if (currentPath is null) return;
        if (!paths.Any(p => string.Equals(p, currentPath, StringComparison.OrdinalIgnoreCase)))
            return;

        var wasPlaying = engine.IsPlaying;
        var position   = engine.GetPosition();

        try
        {
            engine.Load(currentPath);
            if (position > 0)
                engine.Seek(position);
            if (wasPlaying)
                engine.Toggle();
        }
        catch { }

        UpdateUiState();
    }
}
