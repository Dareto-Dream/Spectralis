namespace Spectralis.Core.Notepads;

/// <summary>A single notepad, either session-only or embedded into an audio file's tags.</summary>
public sealed record EmbeddedNotepad(string Id, string Title, string Content, DateTimeOffset UpdatedUtc);
