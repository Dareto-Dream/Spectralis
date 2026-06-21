namespace Spectralis.Core.Metadata;

public sealed class TagEditorModel
{
    public string FilePath { get; init; } = "";
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? AlbumArtist { get; set; }
    public string? Album { get; set; }
    public uint TrackNumber { get; set; }
    public uint DiscNumber { get; set; }
    public uint Year { get; set; }
    public string? Genre { get; set; }
    public string? Comment { get; set; }
    public string? Lyrics { get; set; }
    public string? Composer { get; set; }
    public uint Bpm { get; set; }
    public byte[]? CoverArt { get; set; }
}

/// <summary>TagLib read/write-back with a one-time .bak backup per file.</summary>
public static class TagEditorService
{
    public static TagEditorModel Read(string path)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        return new TagEditorModel
        {
            FilePath = path,
            Title = tag.Title,
            Artist = tag.FirstPerformer,
            AlbumArtist = tag.FirstAlbumArtist,
            Album = tag.Album,
            TrackNumber = tag.Track,
            DiscNumber = tag.Disc,
            Year = tag.Year,
            Genre = tag.FirstGenre,
            Comment = tag.Comment,
            Lyrics = tag.Lyrics,
            Composer = tag.FirstComposer,
            Bpm = tag.BeatsPerMinute,
            CoverArt = tag.Pictures.Length > 0 ? tag.Pictures[0].Data.Data : null,
        };
    }

    public static void Write(TagEditorModel model)
    {
        var bakPath = model.FilePath + ".bak";
        if (!File.Exists(bakPath))
        {
            File.Copy(model.FilePath, bakPath);
        }

        using var file = TagLib.File.Create(model.FilePath);
        var tag = file.Tag;

        tag.Title = model.Title;
        tag.Performers = model.Artist is not null ? [model.Artist] : [];
        tag.AlbumArtists = model.AlbumArtist is not null ? [model.AlbumArtist] : [];
        tag.Album = model.Album;
        tag.Track = model.TrackNumber;
        tag.Disc = model.DiscNumber;
        tag.Year = model.Year;
        tag.Genres = model.Genre is not null ? [model.Genre] : [];
        tag.Comment = model.Comment;
        tag.Lyrics = model.Lyrics;
        tag.Composers = model.Composer is not null ? [model.Composer] : [];
        tag.BeatsPerMinute = model.Bpm;

        if (model.CoverArt is { } art)
        {
            tag.Pictures =
            [
                new TagLib.Picture(new TagLib.ByteVector(art))
                {
                    Type = TagLib.PictureType.FrontCover,
                    MimeType = "image/jpeg",
                },
            ];
        }
        else
        {
            tag.Pictures = [];
        }

        file.Save();
    }

    /// <summary>Writes only the Lyrics tag (USLT/plain) without touching other fields.</summary>
    public static void WriteLyrics(string path, string? lyrics)
    {
        using var file = TagLib.File.Create(path);
        file.Tag.Lyrics = lyrics;
        file.Save();
    }
}
