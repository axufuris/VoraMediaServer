using System.Text.Json;
using Vora.Application.Media.Dtos;

namespace Vora.Application.Media;

public sealed record HeldEpisode(int SeasonNumber, int EpisodeNumber, int? EndEpisodeNumber);

public static class UpcomingEpisodes
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static string? WithoutHeld(string? upcomingJson, IReadOnlyCollection<HeldEpisode> held)
    {
        if (string.IsNullOrWhiteSpace(upcomingJson) || held.Count == 0) return upcomingJson;

        List<UpcomingEpisodeDto>? upcoming;
        try
        {
            upcoming = JsonSerializer.Deserialize<List<UpcomingEpisodeDto>>(upcomingJson, ReadOptions);
        }
        catch (JsonException)
        {
            return upcomingJson;
        }

        if (upcoming == null || upcoming.Count == 0) return upcomingJson;

        var notHeld = upcoming.Where(episode => !IsHeld(episode, held)).ToList();
        return notHeld.Count == upcoming.Count ? upcomingJson : JsonSerializer.Serialize(notHeld);
    }

    private static bool IsHeld(UpcomingEpisodeDto episode, IReadOnlyCollection<HeldEpisode> held) =>
        held.Any(file => file.SeasonNumber == episode.SeasonNumber
            && episode.EpisodeNumber >= file.EpisodeNumber
            && episode.EpisodeNumber <= Math.Max(file.EpisodeNumber, file.EndEpisodeNumber ?? file.EpisodeNumber));
}
