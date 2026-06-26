using System.IO;

namespace Spectralis;

public partial class Form1
{
    private readonly SongWarsTournamentStore songWarsStore = new();
    private ToolStripMenuItem? songWarsMenuItem;
    private SongWarsDialog? activeSongWarsDialog;

    private void InitializeSongWars()
    {
        songWarsMenuItem = new ToolStripMenuItem
        {
            Text = "Song Wars...",
            Name = "songWarsMenuItem"
        };
        songWarsMenuItem.Click += (_, _) => ShowSongWarsDialog();

        toolsToolStripMenuItem.DropDownItems.Add(songWarsMenuItem);
    }

    private void ShowSongWarsDialog()
    {
        if (activeSongWarsDialog is { IsDisposed: false, Visible: true })
        {
            activeSongWarsDialog.Activate();
            return;
        }

        activeSongWarsDialog?.Dispose();
        activeSongWarsDialog = new SongWarsDialog(themePalette, songWarsStore, PlaySongWarsTrack);
        activeSongWarsDialog.FormClosed += (_, _) => activeSongWarsDialog = null;
        activeSongWarsDialog.Show(this);
    }

    private void PlaySongWarsTrack(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            engine.Stop();
            UpdateUiState();
            return;
        }

        if (!File.Exists(filePath))
            return;

        try
        {
            engine.Load(filePath);
            engine.Play();
            UpdateUiState();
        }
        catch (Exception ex)
        {
            ShowError($"Could not play Song Wars track:\n\n{ex.Message}", "Song Wars Playback Error");
        }
    }

    private void DisposeSongWars()
    {
        if (activeSongWarsDialog is { IsDisposed: false })
        {
            activeSongWarsDialog.Close();
            activeSongWarsDialog.Dispose();
        }

        activeSongWarsDialog = null;
    }
}
