using ReactiveUI;

namespace Spectralis.App.ViewModels;

/// <summary>One open notepad: freeform text usable while audio plays, optionally tied to an audio
/// file's embedded tags (see <see cref="Spectralis.Core.Notepads.EmbeddedNotepadService"/>).</summary>
public sealed class NotepadViewModel : ViewModelBase
{
    private string _title = "Notepad";
    private string _content = string.Empty;
    private string? _sourceTrackPath;

    public NotepadViewModel(string? id = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N");
    }

    public string Id { get; }

    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string Content
    {
        get => _content;
        set => this.RaiseAndSetIfChanged(ref _content, value);
    }

    /// <summary>Path of the audio file this notepad was loaded from (or last saved to), if any.</summary>
    public string? SourceTrackPath
    {
        get => _sourceTrackPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _sourceTrackPath, value);
            this.RaisePropertyChanged(nameof(IsFromTrack));
        }
    }

    public bool IsFromTrack => _sourceTrackPath is not null;
}
