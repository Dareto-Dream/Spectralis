using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using Spectralis.Core.Notepads;

namespace Spectralis.App.ViewModels;

/// <summary>
/// Owns every open notepad for the session. Notepads are freeform and usable while audio plays
/// (e.g. tracking a manual queue, jotting improvement notes). Any notepad can be saved into the
/// currently playing local audio file's tags; when a file with embedded notepads is played again,
/// they're reloaded here automatically so the note resurfaces.
/// </summary>
public sealed class NotepadsViewModel : ViewModelBase
{
    private NotepadViewModel? _selectedNotepad;
    private string _statusMessage = string.Empty;

    public ObservableCollection<NotepadViewModel> Notepads { get; } = [];

    public NotepadViewModel? SelectedNotepad
    {
        get => _selectedNotepad;
        set => this.RaiseAndSetIfChanged(ref _selectedNotepad, value);
    }

    /// <summary>Short feedback shown after a save attempt (success or why it couldn't).</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ReactiveCommand<Unit, Unit> NewNotepadCommand { get; }

    /// <summary>Raised when loading a track surfaces embedded notepad(s) not already open — the view should show the panel.</summary>
    public event Action? NotepadsAvailableForCurrentTrack;

    /// <summary>Raised when the user asks to pop a notepad out into its own window.</summary>
    public event Action<NotepadViewModel>? PopOutRequested;

    public NotepadsViewModel()
    {
        NewNotepadCommand = ReactiveCommand.Create(() => { NewNotepad(); });
    }

    public NotepadViewModel NewNotepad(string? title = null, string? content = null)
    {
        var notepad = new NotepadViewModel { Title = title ?? $"Notepad {Notepads.Count + 1}", Content = content ?? string.Empty };
        Notepads.Add(notepad);
        SelectedNotepad = notepad;
        return notepad;
    }

    public void CloseNotepad(NotepadViewModel notepad)
    {
        Notepads.Remove(notepad);
        if (SelectedNotepad == notepad)
            SelectedNotepad = Notepads.Count > 0 ? Notepads[^1] : null;
    }

    public void RequestPopOut(NotepadViewModel notepad) => PopOutRequested?.Invoke(notepad);

    /// <summary>Embeds the notepad's current text into the given local audio file's tags.</summary>
    public void SaveToTrack(NotepadViewModel notepad, string trackPath)
    {
        var embedded = new EmbeddedNotepad(notepad.Id, notepad.Title, notepad.Content, DateTimeOffset.UtcNow);
        EmbeddedNotepadService.UpsertNotepad(trackPath, embedded);
        notepad.SourceTrackPath = trackPath;
    }

    /// <summary>
    /// Reads any notepads embedded in the given track's tags and opens the ones not already open
    /// (matched by Id + source path, so re-visiting the same track doesn't duplicate tabs).
    /// Never closes notepads from other tracks or ad-hoc ones the user created.
    /// </summary>
    public void LoadEmbeddedNotepadsForTrack(string? trackPath)
    {
        if (string.IsNullOrWhiteSpace(trackPath) || !File.Exists(trackPath)) return;

        IReadOnlyList<EmbeddedNotepad> embedded;
        try { embedded = EmbeddedNotepadService.ReadFromAudioTags(trackPath); }
        catch { return; }

        if (embedded.Count == 0) return;

        var opened = false;
        foreach (var e in embedded)
        {
            var alreadyOpen = Notepads.Any(n =>
                n.Id == e.Id && string.Equals(n.SourceTrackPath, trackPath, StringComparison.OrdinalIgnoreCase));
            if (alreadyOpen) continue;

            var notepad = new NotepadViewModel(e.Id)
            {
                Title = e.Title,
                Content = e.Content,
                SourceTrackPath = trackPath,
            };
            Notepads.Add(notepad);
            SelectedNotepad = notepad;
            opened = true;
        }

        if (opened)
            NotepadsAvailableForCurrentTrack?.Invoke();
    }
}
