using System.Text.Json;
using System.Text.Json.Serialization;
using TagLib;
using TagLib.Id3v2;

namespace Spectralis.Core.Notepads;

/// <summary>
/// Reads/writes notepads embedded in an audio file's ID3v2 tags (a single TXXX frame,
/// description "DELTA_NOTEPADS", holding a JSON array). Lets a note travel with the file when
/// it's shared, and reappear automatically the next time the file is played.
/// </summary>
public static class EmbeddedNotepadService
{
    private const string FrameDescription = "DELTA_NOTEPADS";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<EmbeddedNotepad> ReadFromAudioTags(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);
            var id3Tag = file.GetTag(TagTypes.Id3v2, false) as TagLib.Id3v2.Tag;
            return ReadFromTag(id3Tag);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<EmbeddedNotepad> ReadFromTag(TagLib.Id3v2.Tag? id3Tag)
    {
        if (id3Tag is null) return [];

        var frame = id3Tag.GetFrames<UserTextInformationFrame>()
            .FirstOrDefault(f => string.Equals(f.Description, FrameDescription, StringComparison.OrdinalIgnoreCase));
        if (frame is null) return [];

        var payload = string.Join(Environment.NewLine, frame.Text.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (string.IsNullOrWhiteSpace(payload)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<EmbeddedNotepadDto>>(payload, JsonOptions)
                ?.Select(dto => dto.ToModel())
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList()
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Adds or replaces (by Id) a single notepad in the file's embedded set, then rewrites the frame.</summary>
    public static void UpsertNotepad(string audioPath, EmbeddedNotepad notepad)
    {
        using var file = TagLib.File.Create(audioPath);
        var id3Tag = (TagLib.Id3v2.Tag)file.GetTag(TagTypes.Id3v2, true);

        var existing = ReadFromTag(id3Tag).ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        existing[notepad.Id] = notepad;

        WriteAll(id3Tag, existing.Values);
        file.Save();
    }

    public static void RemoveNotepad(string audioPath, string notepadId)
    {
        using var file = TagLib.File.Create(audioPath);
        var id3Tag = (TagLib.Id3v2.Tag)file.GetTag(TagTypes.Id3v2, true);

        var existing = ReadFromTag(id3Tag).Where(n => !string.Equals(n.Id, notepadId, StringComparison.OrdinalIgnoreCase)).ToList();
        WriteAll(id3Tag, existing);
        file.Save();
    }

    private static void WriteAll(TagLib.Id3v2.Tag id3Tag, IEnumerable<EmbeddedNotepad> notepads)
    {
        var list = notepads.ToList();
        var frame = UserTextInformationFrame.Get(id3Tag, FrameDescription, true);

        if (list.Count == 0)
        {
            id3Tag.RemoveFrame(frame);
            return;
        }

        var payload = JsonSerializer.Serialize(list.Select(EmbeddedNotepadDto.FromModel), JsonOptions);
        frame.Text = [payload];
    }

    private sealed class EmbeddedNotepadDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("updatedUtc")] public DateTimeOffset UpdatedUtc { get; set; }

        public EmbeddedNotepad? ToModel() =>
            string.IsNullOrWhiteSpace(Id) ? null : new EmbeddedNotepad(Id, Title ?? "Notepad", Content ?? "", UpdatedUtc);

        public static EmbeddedNotepadDto FromModel(EmbeddedNotepad n) => new()
        {
            Id = n.Id,
            Title = n.Title,
            Content = n.Content,
            UpdatedUtc = n.UpdatedUtc,
        };
    }
}
