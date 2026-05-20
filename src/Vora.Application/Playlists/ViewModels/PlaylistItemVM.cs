using System;
using System.Collections.Generic;
using System.Text;

namespace Vora.Application.Playlists.ViewModels;

public class PlaylistItemVM
{
    public Guid Id { get; set; }
    public Guid MediaItemId { get; set; }
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TvShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? ReleaseYear { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public int? DurationMinutes { get; set; }
    public string? ContentRating { get; set; }

    public bool IsPlayed { get; set; }
    public double ResumePositionSeconds { get; set; }

    public string? ArtistName { get; set; }
    public string? AlbumTitle { get; set; }
    public Guid? AlbumId { get; set; }
    public string? AlbumArtworkUrl { get; set; }
    public int? TrackNumber { get; set; }
    public int? DurationSeconds { get; set; }
}