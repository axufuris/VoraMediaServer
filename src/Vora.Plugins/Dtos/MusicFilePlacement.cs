namespace Vora.Plugins.Dtos;

public sealed record MusicFilePlacement(string FilePath, Guid AlbumId, Guid TrackId, string TrackTitle, string? OriginalTitle, int? DiscNumber);
