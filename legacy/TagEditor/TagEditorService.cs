using System.IO;

namespace Spectralis;

internal static class TagEditorService
{
    public static TagEditorModel Read(string path)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        return new TagEditorModel
        {
            FilePath    = path,
            Title       = tag.Title,
            Artist      = tag.FirstPerformer,
            AlbumArtist = tag.FirstAlbumArtist,
            Album       = tag.Album,
            TrackNumber = tag.Track,
            DiscNumber  = tag.Disc,
            Year        = tag.Year,
            Genre       = tag.FirstGenre,
            Comment     = tag.Comment,
            Composer    = tag.FirstComposer,
            BPM         = tag.BeatsPerMinute,
            CoverArt    = tag.Pictures.Length > 0 ? tag.Pictures[0].Data.Data : null,
        };
    }

    public static void Write(TagEditorModel model)
    {
        var bakPath = model.FilePath + ".bak";
        if (!File.Exists(bakPath))
            File.Copy(model.FilePath, bakPath);

        using var file = TagLib.File.Create(model.FilePath);
        var tag = file.Tag;

        tag.Title         = model.Title;
        tag.Performers    = model.Artist      is not null ? [model.Artist]      : [];
        tag.AlbumArtists  = model.AlbumArtist is not null ? [model.AlbumArtist] : [];
        tag.Album         = model.Album;
        tag.Track         = model.TrackNumber;
        tag.Disc          = model.DiscNumber;
        tag.Year          = model.Year;
        tag.Genres        = model.Genre       is not null ? [model.Genre]       : [];
        tag.Comment       = model.Comment;
        tag.Composers     = model.Composer    is not null ? [model.Composer]    : [];
        tag.BeatsPerMinute = model.BPM;

        if (model.CoverArt is { } art)
        {
            tag.Pictures =
            [
                new TagLib.Picture(new TagLib.ByteVector(art))
                {
                    Type     = TagLib.PictureType.FrontCover,
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
}
