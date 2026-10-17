using Vora.Application.Analysis.Results;
using Vora.Application.Streaming.Dtos;

namespace Vora.Application.Streaming;

public sealed record VideoTrackGeometry(Guid TrackId, int? Width, int? Height, double? FrameRate, string? HdrType);

public static class VideoTrackGeometryFill
{
    public static bool IsMissing(MediaPartStreamInfoDto part) =>
        !string.IsNullOrWhiteSpace(part.FilePath) && part.VideoTracks.Any(track => track.Width is null || track.Height is null);

    public static List<VideoTrackGeometry> Apply(MediaPartStreamInfoDto part, IReadOnlyList<VideoStreamFacts> facts)
    {
        var updates = new List<VideoTrackGeometry>();
        foreach (var track in part.VideoTracks)
        {
            var fact = facts.FirstOrDefault(f => f.StreamIndex == track.StreamIndex);
            if (fact is null || fact.Width is null || fact.Height is null) continue;

            track.Width = fact.Width;
            track.Height = fact.Height;
            track.FrameRate = fact.FrameRate;
            if (DynamicRange.HasNoKnownBaseLayer(track.HdrType)) track.HdrType = fact.HdrType;
            updates.Add(new VideoTrackGeometry(track.Id, track.Width, track.Height, track.FrameRate, track.HdrType));
        }
        return updates;
    }
}

public static class DynamicRange
{
    public static bool HasNoKnownBaseLayer(string? hdrType) =>
        string.Equals(hdrType?.Trim(), "DoVi", StringComparison.OrdinalIgnoreCase);
}
