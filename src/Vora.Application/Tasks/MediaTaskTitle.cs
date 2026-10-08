using System.Linq.Expressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tasks;

public sealed record MediaTaskTitleParts(string Title, string? ShowTitle, int? SeasonNumber, int? EpisodeNumber);

public static class MediaTaskTitle
{
    public static readonly Expression<Func<MediaItem, MediaTaskTitleParts>> Projection = m => new MediaTaskTitleParts(
        m.Title,
        m is Episode ? ((Episode)m).Season.TvShow.Title : m is Season ? ((Season)m).TvShow.Title : null,
        m is Episode ? (int?)((Episode)m).Season.SeasonNumber : m is Season ? (int?)((Season)m).SeasonNumber : null,
        m is Episode ? (int?)((Episode)m).EpisodeNumber : null);

    public static string? Format(MediaTaskTitleParts? parts)
    {
        if (parts == null || string.IsNullOrWhiteSpace(parts.Title)) return null;
        if (string.IsNullOrWhiteSpace(parts.ShowTitle)) return parts.Title;

        return parts is { SeasonNumber: int season, EpisodeNumber: int episode }
            ? $"{parts.ShowTitle} - S{season:D2}E{episode:D2} - {parts.Title}"
            : $"{parts.ShowTitle} - {parts.Title}";
    }
}
