using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Playlists.ViewModels;

public class PlaylistSummaryVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ItemCount { get; set; }
    public PlaylistMediaType MediaType { get; set; } = PlaylistMediaType.Mixed;

    // The owner's uploaded cover, or null to build one from PosterUrls.
    public string? ImageUrl { get; set; }

    // Up to four DIFFERENT images, in playlist order, for the cover mosaic. The
    // query takes a few more than four and this drops the repeats, so a
    // playlist that opens with four songs from one album still gets a mosaic
    // of four albums rather than one cover four times.
    private List<string> _posterUrls = new();
    public List<string> PosterUrls
    {
        get => _posterUrls;
        set => _posterUrls = value.Distinct().Take(MosaicSize).ToList();
    }

    public const int MosaicSize = 4;
    public const int MosaicCandidates = 24;
    public List<string> BackdropUrls { get; set; } = new();

    public bool IsShared { get; set; }

    // Whether the profile asking owns it. A shared playlist opened by anyone
    // else is read-only, and a client needs this to hide the edit controls
    // rather than letting them fail against an owner-only endpoint.
    public bool IsOwner { get; set; }
    public string OwnerName { get; set; } = string.Empty;
}