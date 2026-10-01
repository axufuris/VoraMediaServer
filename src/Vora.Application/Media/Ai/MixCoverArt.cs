using Vora.Domain.Entities.Media;

namespace Vora.Application.Media.Ai;

public static class MixCoverArt
{
    public const int MosaicSize = 4;
    public const int TracksSampled = 40;

    public static bool UsesMosaic(GeneratedMixKind kind) =>
        kind is GeneratedMixKind.AiPlaylist or GeneratedMixKind.Bridge or GeneratedMixKind.Blend or GeneratedMixKind.Requested;

    public static List<string> Pick(IEnumerable<AiTrackCandidate> tracksInOrder)
    {
        var picked = new List<string>();
        var artists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sameArtist = new List<string>();

        foreach (var track in tracksInOrder)
        {
            var url = track.ArtworkUrl;
            if (string.IsNullOrWhiteSpace(url) || picked.Contains(url)) continue;

            if (artists.Add(track.ArtistKey))
            {
                picked.Add(url);
                if (picked.Count == MosaicSize) return picked;
            }
            else if (!sameArtist.Contains(url))
            {
                sameArtist.Add(url);
            }
        }

        foreach (var url in sameArtist)
        {
            if (picked.Count == MosaicSize) break;
            if (!picked.Contains(url)) picked.Add(url);
        }

        return picked;
    }
}
