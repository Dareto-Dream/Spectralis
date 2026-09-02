namespace Spectralis.App.VideoExport;

/// <summary>The track a video export renders — its audio plus the metadata the overlays draw.</summary>
public sealed record VideoExportRequest(
    string AudioFilePath,
    string Title,
    string Artist,
    string Album,
    byte[]? AlbumArtBytes,
    string? AlbumArtMimeType);
