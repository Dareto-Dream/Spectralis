using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Spectralis.Audio;
using Spectralis.Library;

namespace Spectralis.UI
{
    public class LibraryDragDropHandler
    {
        private readonly LibraryView _libraryView;
        private readonly PlaylistPanel _playlistPanel;

        public event EventHandler<IReadOnlyList<TrackInfo>> TracksDropped;

        public LibraryDragDropHandler(LibraryView libraryView, PlaylistPanel playlistPanel)
        {
            _libraryView = libraryView;
            _playlistPanel = playlistPanel;

            _libraryView.MouseDown += OnLibraryMouseDown;
            _libraryView.MouseMove += OnLibraryMouseMove;

            _playlistPanel.AllowDrop = true;
            _playlistPanel.DragEnter += OnPlaylistDragEnter;
            _playlistPanel.DragDrop += OnPlaylistDragDrop;
        }

        private Point _dragStart;
        private bool _dragPending;
        private IReadOnlyList<TrackInfo> _pendingTracks;

        private void OnLibraryMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragStart = e.Location;
                _dragPending = true;
                _pendingTracks = _libraryView.SelectedTracks;
            }
        }

        private void OnLibraryMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragPending || e.Button != MouseButtons.Left)
                return;

            if (Math.Abs(e.X - _dragStart.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - _dragStart.Y) < SystemInformation.DragSize.Height)
                return;

            _dragPending = false;
            if (_pendingTracks == null || _pendingTracks.Count == 0)
                return;

            _libraryView.DoDragDrop(_pendingTracks, DragDropEffects.Copy);
        }

        private void OnPlaylistDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(List<TrackInfo>)) ||
                e.Data.GetDataPresent(typeof(TrackInfo[])))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void OnPlaylistDragDrop(object sender, DragEventArgs e)
        {
            IReadOnlyList<TrackInfo> tracks = null;

            if (e.Data.GetDataPresent(typeof(List<TrackInfo>)))
                tracks = (List<TrackInfo>)e.Data.GetData(typeof(List<TrackInfo>));
            else if (e.Data.GetDataPresent(typeof(TrackInfo[])))
                tracks = (TrackInfo[])e.Data.GetData(typeof(TrackInfo[]));

            if (tracks != null && tracks.Count > 0)
                TracksDropped?.Invoke(this, tracks);
        }
    }
}
