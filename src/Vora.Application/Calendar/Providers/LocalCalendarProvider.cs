using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Application.Calendar.Dtos;
using Vora.Application.Media.Dtos;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Calendar.Providers;

public class LocalCalendarProvider(
    ICalendarRepository repository,
    ILogger<LocalCalendarProvider> logger) : ICalendarProvider
{
    private const string TmdbDiscoveryProviderId = "tmdb_discovery";
    private const string UnratedRating = "Unrated";

    public string Id => "local_calendar";
    public string Name => "Vora Local Calendar";
    public string Description => "Generates calendar events natively from your existing library and pending watchlist requests.";
    public bool IsSystemPlugin => true;
    public string Version => "1.0.0";
    public string Type => "Calendar";
    public string DeveloperName => "System";

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() =>
        new List<PluginSettingDefinitionDto>();

    public async Task<IEnumerable<CalendarEventDto>> GetEventsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        var events = new List<CalendarEventDto>();

        events.AddRange(await BuildMovieEventsAsync(startDate, endDate));
        events.AddRange(await BuildShowEventsAsync(startDate, endDate));
        events.AddRange(await BuildRequestEventsAsync(startDate, endDate));
        events.AddRange(await BuildWatchlistEventsAsync(startDate, endDate));

        return events;
    }

    private async Task<IEnumerable<CalendarEventDto>> BuildMovieEventsAsync(DateTime startDate, DateTime endDate)
    {
        var events = new List<CalendarEventDto>();
        var movies = await repository.GetMoviesReleasingInRangeAsync(startDate, endDate);

        foreach (var movie in movies)
        {
            if (movie.TheatricalReleaseDate >= startDate && movie.TheatricalReleaseDate <= endDate)
            {
                events.Add(MapMovieToEvent(movie, movie.TheatricalReleaseDate.Value, "Theatrical"));
            }

            if (movie.DigitalReleaseDate >= startDate && movie.DigitalReleaseDate <= endDate)
            {
                events.Add(MapMovieToEvent(movie, movie.DigitalReleaseDate.Value, "Digital"));
            }
        }

        return events;
    }

    private async Task<IEnumerable<CalendarEventDto>> BuildShowEventsAsync(DateTime startDate, DateTime endDate)
    {
        var events = new List<CalendarEventDto>();
        var shows = await repository.GetActiveShowsWithUpcomingEpisodesAsync();

        foreach (var show in shows)
        {
            var upcoming = TryParseUpcomingEpisodes(show);
            if (upcoming == null)
            {
                continue;
            }

            foreach (var ep in upcoming.Where(e => e.AirDate >= startDate && e.AirDate <= endDate))
            {
                events.Add(new CalendarEventDto
                {
                    Id = $"local_tv_{show.Id}_{ep.SeasonNumber}x{ep.EpisodeNumber}",
                    LibraryId = show.LibraryId,
                    ExternalId = show.TmdbId,
                    ExternalProviderId = TmdbDiscoveryProviderId,
                    LibraryItemId = show.Id,
                    Title = show.Title,
                    SubTitle = $"S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2} - {ep.Title}",
                    MediaType = "Episode",
                    ReleaseDate = ep.AirDate,
                    AirTime = ep.AirTime,
                    ReleaseType = "TV Airing",
                    ContentRating = show.ContentRating ?? UnratedRating,
                    PosterUrl = show.PosterUrl,
                    BackgroundUrl = show.BackgroundUrl,
                    IsInLibrary = true,
                    IsWatchlisted = false
                });
            }
        }

        return events;
    }

    private async Task<IEnumerable<CalendarEventDto>> BuildRequestEventsAsync(DateTime startDate, DateTime endDate)
    {
        var requests = await repository.GetRequestsReleasingInRangeAsync(startDate, endDate);

        return requests
            .Where(r => r.ExpectedReleaseDate.HasValue)
            .Select(r => new CalendarEventDto
            {
                Id = $"local_req_{r.Id}",
                ExternalId = r.ExternalId,
                ExternalProviderId = r.ProviderId,
                LibraryItemId = null,
                Title = r.Title,
                SubTitle = "Watchlist Request",
                MediaType = r.Type,
                ReleaseDate = r.ExpectedReleaseDate!.Value,
                ReleaseType = "Release",
                ContentRating = UnratedRating,
                PosterUrl = r.PosterUrl,
                IsInLibrary = false,
                IsWatchlisted = true
            });
    }

    private async Task<IEnumerable<CalendarEventDto>> BuildWatchlistEventsAsync(DateTime startDate, DateTime endDate)
    {
        var items = await repository.GetWatchlistItemsReleasingInRangeAsync(startDate, endDate);

        return items
            .Where(i => i.ExpectedReleaseDate.HasValue)
            .Select(i => new CalendarEventDto
            {
                Id = $"local_watch_{i.Id}",
                ExternalId = i.ExternalId,
                ExternalProviderId = i.ProviderId,
                LibraryItemId = null,
                Title = i.Title,
                SubTitle = "Watchlist",
                MediaType = i.Type,
                ReleaseDate = i.ExpectedReleaseDate!.Value,
                ReleaseType = "Release",
                ContentRating = UnratedRating,
                PosterUrl = i.PosterUrl,
                IsInLibrary = false,
                IsWatchlisted = true
            });
    }

    private static CalendarEventDto MapMovieToEvent(CalendarMovieSourceDto movie, DateTime releaseDate, string releaseType) => new()
    {
        Id = $"local_movie_{movie.Id}_{releaseType}",
        LibraryId = movie.LibraryId,
        ExternalId = movie.TmdbId,
        ExternalProviderId = TmdbDiscoveryProviderId,
        LibraryItemId = movie.Id,
        Title = movie.Title,
        SubTitle = null,
        MediaType = "Movie",
        ReleaseDate = releaseDate,
        ReleaseType = releaseType,
        ContentRating = movie.ContentRating ?? UnratedRating,
        PosterUrl = movie.PosterUrl,
        BackgroundUrl = movie.BackgroundUrl,
        IsInLibrary = true,
        IsWatchlisted = false
    };

    private List<UpcomingEpisodeDto>? TryParseUpcomingEpisodes(CalendarShowSourceDto show)
    {
        try
        {
            return JsonSerializer.Deserialize<List<UpcomingEpisodeDto>>(show.UpcomingEpisodesJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Skipping show {ShowId}: corrupted UpcomingEpisodesJson.", show.Id);
            return null;
        }
    }
}
