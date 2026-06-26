namespace Spectralis;

public partial class Form1
{
    private enum ContentWorkspace
    {
        NowPlaying,
        Library,
        Playlists
    }

    private ToolStripMenuItem? mniNowPlaying;
    private ContentWorkspace activeWorkspace = ContentWorkspace.NowPlaying;

    private bool IsBrowserWorkspaceActive =>
        activeWorkspace is ContentWorkspace.Library or ContentWorkspace.Playlists;

    private ToolStripMenuItem CreateNowPlayingMenuItem()
    {
        mniNowPlaying = new ToolStripMenuItem
        {
            Name = "mniNowPlaying",
            Text = "&Now Playing"
        };
        mniNowPlaying.Click += (_, _) => ShowNowPlayingView();
        return mniNowPlaying;
    }

    private void ShowNowPlayingView() => SetContentWorkspace(ContentWorkspace.NowPlaying);

    private void SetContentWorkspace(ContentWorkspace workspace)
    {
        activeWorkspace = workspace;
        isLibraryVisible = workspace == ContentWorkspace.Library;
        isPlaylistsVisible = workspace == ContentWorkspace.Playlists;

        if (libraryBrowser is not null)
        {
            libraryBrowser.Visible = isLibraryVisible;
            if (isLibraryVisible)
                libraryBrowser.BringToFront();
        }

        if (playlistBrowser is not null)
        {
            playlistBrowser.Visible = isPlaylistsVisible;
            if (isPlaylistsVisible)
                playlistBrowser.BringToFront();
        }

        if (workspace == ContentWorkspace.NowPlaying)
        {
            RestoreNowPlayingContent();
        }
        else
        {
            HideNowPlayingContentForBrowser();
        }

        UpdateWorkspaceMenuState();
    }

    private void HideNowPlayingContentForBrowser()
    {
        visualizerControl.Visible = false;
        visualizerNavPanel.Visible = false;
        embeddedContentControl?.Hide();
        capsuleStoryControl?.Hide();
        if (youTubeVideoWebView is not null)
            youTubeVideoWebView.Visible = false;
    }

    private void RestoreNowPlayingContent()
    {
        if (capsuleStoryControl is { HasStory: true, Visible: true })
        {
            capsuleStoryControl.BringToFront();
            return;
        }

        if (youTubeVideoMode && youTubeVideoWebView is not null)
        {
            ApplyYouTubeVideoVisibility();
            return;
        }

        UpdateEmbeddedContent(engine.CurrentTrack, force: true);
        if (embeddedContentControl is { Visible: true })
            embeddedContentControl.BringToFront();
        else
        {
            visualizerControl.Visible = true;
            visualizerNavPanel.Visible = true;
            visualizerControl.BringToFront();
        }
    }

    private void UpdateWorkspaceMenuState()
    {
        if (mniNowPlaying is not null)
            mniNowPlaying.Checked = activeWorkspace == ContentWorkspace.NowPlaying;
        if (mniLibrary is not null)
            mniLibrary.Checked = activeWorkspace == ContentWorkspace.Library;
        if (mniPlaylists is not null)
            mniPlaylists.Checked = activeWorkspace == ContentWorkspace.Playlists;
    }
}
