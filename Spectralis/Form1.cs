using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Spectralis.Audio;

namespace Spectralis
{
    public partial class Form1 : Form
    {
        private AudioEngine _engine;
        private DeviceManager _deviceManager;

        public Form1()
        {
            InitializeComponent();
            InitAudio();
            SetupEventHandlers();
            AllowDrop = true;
        }

        private void InitAudio()
        {
            _engine = new AudioEngine();
            _deviceManager = new DeviceManager();

            _engine.StateChanged += OnEngineStateChanged;
            _engine.PositionChanged += OnEnginePositionChanged;
            _engine.TrackEnded += OnTrackEnded;
            _engine.PlaybackError += OnPlaybackError;

            volumeBar.Value = (int)(AppSettings.DefaultVolume);
            _engine.Volume = AppSettings.DefaultVolume / 100f;
        }

        private void SetupEventHandlers()
        {
            openToolStripMenuItem.Click += OnOpenFile;
            btnPlay.Click += OnPlay;
            btnPause.Click += OnPause;
            btnStop.Click += OnStop;
            btnPrev.Click += OnPrev;
            btnNext.Click += OnNext;
            progressBar.Scroll += OnProgressScroll;
            volumeBar.Scroll += OnVolumeScroll;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        private void OnOpenFile(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = FormatDetector.GetOpenFileFilter(),
                Multiselect = true,
                InitialDirectory = AppSettings.LastOpenDirectory
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            AppSettings.LastOpenDirectory = Path.GetDirectoryName(dlg.FileNames[0]);
            LoadTrack(dlg.FileNames[0]);
        }

        private void LoadTrack(string filePath)
        {
            if (!FormatDetector.IsSupported(filePath))
            {
                statusLabel.Text = $"Unsupported format: {Path.GetExtension(filePath)}";
                return;
            }

            try
            {
                var info = MetadataExtractor.Extract(filePath);
                var reader = FormatDetector.CreateReader(filePath);
                _engine.Load(reader, info);
                UpdateTrackDisplay(info);
                _engine.Play();
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Error loading file: {ex.Message}";
            }
        }

        private void UpdateTrackDisplay(TrackInfo info)
        {
            lblTitle.Text = info.DisplayTitle;
            lblArtist.Text = info.DisplayArtist;
            Text = $"{info.DisplayTitle} — Spectralis";
            statusLabel.Text = $"{info.Format} | {info.Bitrate}kbps | {info.SampleRate}Hz";

            if (info.AlbumArt != null)
                albumArtPanel.AlbumArt = info.AlbumArt;
            else
                albumArtPanel.AlbumArt = null;
        }

        private void OnPlay(object sender, EventArgs e) => _engine.Play();
        private void OnPause(object sender, EventArgs e) => _engine.Pause();
        private void OnStop(object sender, EventArgs e) => _engine.Stop();
        private void OnPrev(object sender, EventArgs e) { }
        private void OnNext(object sender, EventArgs e) { }

        private void OnProgressScroll(object sender, EventArgs e)
        {
            if (_engine.Duration > TimeSpan.Zero)
            {
                var position = TimeSpan.FromSeconds(_engine.Duration.TotalSeconds * progressBar.Value / 1000.0);
                _engine.Position = position;
            }
        }

        private void OnVolumeScroll(object sender, EventArgs e)
        {
            _engine.Volume = volumeBar.Value / 100f;
        }

        private void OnEngineStateChanged(object sender, PlaybackState state)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnEngineStateChanged(sender, state))); return; }
            btnPlay.Enabled = state != PlaybackState.Playing;
            btnPause.Enabled = state == PlaybackState.Playing;
        }

        private void OnEnginePositionChanged(object sender, TimeSpan position)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnEnginePositionChanged(sender, position))); return; }

            if (_engine.Duration > TimeSpan.Zero)
            {
                progressBar.Value = (int)(position.TotalSeconds / _engine.Duration.TotalSeconds * 1000);
                lblTime.Text = $"{FormatTime(position)} / {FormatTime(_engine.Duration)}";
            }
        }

        private void OnTrackEnded(object sender, EventArgs e)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnTrackEnded(sender, e))); return; }
            progressBar.Value = 0;
        }

        private void OnPlaybackError(object sender, Exception ex)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnPlaybackError(sender, ex))); return; }
            statusLabel.Text = $"Playback error: {ex.Message}";
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0)
                LoadTrack(files[0]);
        }

        private static string FormatTime(TimeSpan t)
        {
            return t.Hours > 0
                ? $"{t.Hours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes}:{t.Seconds:D2}";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _engine?.Dispose();
            _deviceManager?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
