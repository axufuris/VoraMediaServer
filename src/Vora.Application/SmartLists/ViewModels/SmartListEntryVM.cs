using Vora.Application.Iptv.ViewModels;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Media.ViewModels;
using Vora.Application.Podcasts.ViewModels;

namespace Vora.Application.SmartLists.ViewModels;

public enum SmartListEntryKind
{
    Media,
    Channel,
    Station,
    PodcastEpisode,
    Album,
    Recording
}

public class SmartListEntryVM
{
    public SmartListEntryKind Kind { get; set; }
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? ImageUrl { get; set; }
    public LibraryItemVM? Media { get; set; }
    public IptvChannelVM? Channel { get; set; }
    public PodcastFeedEpisodeVM? PodcastEpisode { get; set; }
    public AlbumVM? Album { get; set; }
    public IptvRecordingSessionVM? Recording { get; set; }
}
