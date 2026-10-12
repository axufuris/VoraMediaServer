using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

public static class CoverImageTracks
{
    private static readonly HashSet<string> StillImageCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "mjpeg", "png", "bmp", "gif", "tiff", "webp"
    };

    public static bool IsStillImageCodec(string? codec) => codec != null && StillImageCodecs.Contains(codec);

    public static List<T> Without<T>(IEnumerable<T> videoTracks, Func<T, string?> codec)
    {
        var all = videoTracks.ToList();
        var moving = all.Where(track => !IsStillImageCodec(codec(track))).ToList();
        return moving.Count > 0 ? moving : all;
    }

    public static MediaVideoTrack? MainVideoTrack(IEnumerable<MediaVideoTrack>? videoTracks)
    {
        if (videoTracks == null) return null;

        return Without(videoTracks, track => track.Codec)
            .OrderByDescending(track => track.IsDefault)
            .ThenBy(track => track.StreamIndex)
            .FirstOrDefault();
    }
}
