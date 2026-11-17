using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.Playlists;

namespace Spectralis.App.ViewModels;

public sealed partial class PlaylistsViewModel
{
    private CancellationTokenSource? _playlistJob;
    private string _operationStatus = string.Empty;
    public string OperationStatus { get => _operationStatus; private set => this.RaiseAndSetIfChanged(ref _operationStatus, value); }
    public bool IsPlaylistBusy => _playlistJob is not null;
    public string SelectedNotice => SelectedRow is { } row && FindPlaylist(row.Id) is { } playlist
        ? string.Join(" ", new[] { playlist.ImportNotice, playlist.OfflineDirectory is null ? null : $"{playlist.Items.Count(i => i.PlayableReference(true) is not null)}/{playlist.Items.Count} tracks available offline." }.Where(s => !string.IsNullOrWhiteSpace(s)))
        : string.Empty;

    public void CancelPlaylistJob() => _playlistJob?.Cancel();

    public Task<Playlist> ResolvePlaylistLinkAsync(string input, CancellationToken ct) =>
        new PlaylistImportService(_settings).ImportAsync(input, ct);

    public async Task ImportLinkAsync(string input)
    {
        if (IsPlaylistBusy) return;
        using var job = new CancellationTokenSource();
        _playlistJob = job;
        this.RaisePropertyChanged(nameof(IsPlaylistBusy));
        OperationStatus = "Loading playlist…";
        try
        {
            var playlist = await new PlaylistImportService(_settings).ImportAsync(input, job.Token);
            PlaylistStore.Save(playlist);
            Reload();
            SelectedRow = FindRow(playlist.Id);
            OperationStatus = $"Imported {playlist.Items.Count} tracks. {playlist.ImportNotice}".Trim();
        }
        catch (OperationCanceledException) { OperationStatus = "Import cancelled."; }
        catch (Exception ex) { OperationStatus = ex.Message; }
        finally { _playlistJob = null; this.RaisePropertyChanged(nameof(IsPlaylistBusy)); this.RaisePropertyChanged(nameof(SelectedNotice)); }
    }

    public async Task DownloadSelectedAsync(string folder)
    {
        if (IsPlaylistBusy || SelectedRow is not { } row) return;
        var playlist = FindPlaylist(row.Id);
        if (playlist is null) { OperationStatus = "Save a regular playlist to download it offline."; return; }
        using var job = new CancellationTokenSource();
        _playlistJob = job;
        this.RaisePropertyChanged(nameof(IsPlaylistBusy));
        try
        {
            var result = await new PlaylistOfflineService().DownloadAsync(playlist, folder, PlaylistStore.Save,
                new Progress<PlaylistDownloadProgress>(p => OperationStatus = $"Downloading {p.Completed}/{p.Total} · {p.Track}"), job.Token);
            OperationStatus = result.Failures.Count == 0 ? $"Saved {result.Downloaded} tracks for offline playback."
                : $"Saved {result.Downloaded}/{result.Total}. {string.Join("; ", result.Failures.Take(3))} Run Download offline again to retry missing tracks.";
        }
        catch (OperationCanceledException) { OperationStatus = "Download cancelled. Completed tracks are kept; download again to resume."; }
        catch (Exception ex) { OperationStatus = ex.Message; }
        finally { _playlistJob = null; Reload(); SelectedRow = FindRow(row.Id); this.RaisePropertyChanged(nameof(IsPlaylistBusy)); this.RaisePropertyChanged(nameof(SelectedNotice)); }
    }

    public async Task PlayOfflineAsync()
    {
        if (SelectedRow is not { } row || FindPlaylist(row.Id) is not { } playlist) return;
        var items = playlist.Items.Select(i => (Item: i, Path: i.PlayableReference(offlineOnly: true))).Where(i => i.Path is not null).ToList();
        if (items.Count == 0) { OperationStatus = "No offline audio found. Download this playlist first."; return; }
        _setQueueMetadata?.Invoke(items.GroupBy(i => i.Path!).ToDictionary(g => g.Key, g => (g.First().Item.Title ?? "Track", g.First().Item.Artist ?? string.Empty)));
        _applyDefaultVisualizer?.Invoke(playlist.DefaultVisualizer);
        OperationStatus = items.Count == playlist.Items.Count ? "Playing offline." : $"Playing {items.Count}/{playlist.Items.Count} available tracks offline.";
        await _playQueue(items.Select(i => i.Path!).ToList(), 0);
    }
}
