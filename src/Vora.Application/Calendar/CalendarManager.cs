using Microsoft.Extensions.Logging;
using Vora.Application.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Calendar;

public interface ICalendarManager
{
    Task<IEnumerable<CalendarEventDto>> GetCalendarEventsAsync(
        DateTime startDate,
        DateTime endDate,
        bool hasAllAccess,
        List<Guid> allowedLibs,
        bool hasAllRatings,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        bool blockUnrated,
        CancellationToken cancellationToken = default);
}

public class CalendarManager(
    IEnumerable<ICalendarProvider> providers,
    IMediaRepository mediaRepository,
    IPluginSettingsProvider pluginSettings,
    ILogger<CalendarManager> logger) : ICalendarManager
{
    private const string EnabledSettingKey = "is_enabled";
    private const string DisabledValue = "false";
    private const string TmdbDiscoveryProviderId = "tmdb_discovery";
    private const string UnratedRating = "Unrated";
    private const int TimezoneToleranceDays = 2;

    public async Task<IEnumerable<CalendarEventDto>> GetCalendarEventsAsync(
        DateTime startDate,
        DateTime endDate,
        bool hasAllAccess,
        List<Guid> allowedLibs,
        bool hasAllRatings,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        bool blockUnrated,
        CancellationToken cancellationToken = default)
    {
        var allEvents = await CollectEventsFromActiveProvidersAsync(startDate, endDate, cancellationToken);
        var libraryMap = await BuildExternalIdToLibraryMapAsync(allEvents);

        foreach (var ev in allEvents)
        {
            LinkToLibrary(ev, libraryMap);
        }

        var filtered = allEvents
            .Where(ev => PassesContentRating(ev, hasAllRatings, allowedMovieRatings, allowedTvRatings, blockUnrated))
            .Where(ev => PassesLibraryAccess(ev, hasAllAccess, allowedLibs))
            .ToList();

        return DeduplicateAndMerge(filtered);
    }

    private async Task<List<CalendarEventDto>> CollectEventsFromActiveProvidersAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        var activeProviders = new List<ICalendarProvider>();

        foreach (var provider in providers)
        {
            var isEnabledStr = await pluginSettings.GetSettingAsync(provider.Id, EnabledSettingKey);
            if (isEnabledStr != DisabledValue)
            {
                activeProviders.Add(provider);
            }
        }

        var fetchTasks = activeProviders.Select(p => SafeFetchAsync(p, startDate, endDate, cancellationToken));
        var results = await Task.WhenAll(fetchTasks);
        return results.SelectMany(e => e).ToList();
    }

    private async Task<IEnumerable<CalendarEventDto>> SafeFetchAsync(ICalendarProvider provider, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        try
        {
            return await provider.GetEventsAsync(startDate, endDate, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Calendar provider {ProviderId} failed to load events.", provider.Id);
            return Array.Empty<CalendarEventDto>();
        }
    }

    // An event the server already holds carries the id of the item it matched, so
    // a client can open the library page instead of the discovery page for a
    // title it owns.
    public static void LinkToLibrary(CalendarEventDto ev, IReadOnlyDictionary<string, LibraryMatch> libraryMap)
    {
        if (ev.LibraryItemId.HasValue)
        {
            ev.IsInLibrary = true;
            return;
        }

        if (string.IsNullOrEmpty(ev.ExternalId) || !libraryMap.TryGetValue(ev.ExternalId, out var match)) return;

        ev.LibraryItemId = match.MediaItemId;
        ev.LibraryId = match.LibraryId;
        ev.IsInLibrary = true;
    }

    private async Task<Dictionary<string, LibraryMatch>> BuildExternalIdToLibraryMapAsync(IEnumerable<CalendarEventDto> events)
    {
        var externalTmdbIds = events
            .Where(e => !e.LibraryId.HasValue && e.ExternalProviderId == TmdbDiscoveryProviderId && !string.IsNullOrEmpty(e.ExternalId))
            .Select(e => e.ExternalId!)
            .Distinct()
            .ToList();

        if (externalTmdbIds.Count == 0)
        {
            return new Dictionary<string, LibraryMatch>();
        }

        return await mediaRepository.GetLibraryMatchesByTmdbIdsAsync(externalTmdbIds);
    }

    private static bool PassesContentRating(CalendarEventDto ev, bool hasAllRatings, List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated)
    {
        if (hasAllRatings)
        {
            return true;
        }

        var isUnrated = ev.ContentRating.Equals(UnratedRating, StringComparison.OrdinalIgnoreCase);
        if (blockUnrated && isUnrated)
        {
            return false;
        }

        var listToCheck = ev.MediaType.Equals("Movie", StringComparison.OrdinalIgnoreCase)
            ? allowedMovieRatings
            : allowedTvRatings;

        return isUnrated || listToCheck.Contains(ev.ContentRating, StringComparer.OrdinalIgnoreCase);
    }

    // Pure check now that linking happens first: an event in a library the
    // profile cannot see is dropped, and an unmatched one is upcoming rather than
    // owned, so it stays.
    public static bool PassesLibraryAccess(CalendarEventDto ev, bool hasAllAccess, List<Guid> allowedLibs)
    {
        if (hasAllAccess) return true;

        return !ev.LibraryId.HasValue || allowedLibs.Contains(ev.LibraryId.Value);
    }

    private static List<CalendarEventDto> DeduplicateAndMerge(List<CalendarEventDto> events)
    {
        var deduplicated = new List<CalendarEventDto>();
        var groupedByExternalId = events.GroupBy(e => e.ExternalId ?? e.Id);

        foreach (var group in groupedByExternalId)
        {
            if (string.IsNullOrEmpty(group.First().ExternalId))
            {
                deduplicated.AddRange(group);
                continue;
            }

            var eventsForTitle = group.ToList();
            var watchlistEvents = eventsForTitle
                .Where(e => e.IsWatchlisted && (e.SubTitle == "Watchlist" || e.SubTitle == "Watchlist Request"))
                .ToList();
            var concreteEvents = eventsForTitle.Except(watchlistEvents).ToList();

            if (concreteEvents.Count == 0)
            {
                deduplicated.Add(watchlistEvents.OrderBy(e => e.SubTitle).First());
                continue;
            }

            var deduplicatedConcrete = DeduplicateConcreteEvents(concreteEvents);
            MergeWatchlistDots(deduplicatedConcrete, watchlistEvents, deduplicated);
            deduplicated.AddRange(deduplicatedConcrete);
        }

        return deduplicated.OrderBy(e => e.ReleaseDate).ToList();
    }

    private static List<CalendarEventDto> DeduplicateConcreteEvents(List<CalendarEventDto> concreteEvents) =>
        concreteEvents
            .GroupBy(e => $"{e.ReleaseDate:yyyyMMdd}_{e.SubTitle}")
            .Select(g => g
                .OrderByDescending(x => x.IsInLibrary)
                .ThenByDescending(x => x.AirTime != null)
                .First())
            .ToList();

    private static void MergeWatchlistDots(
        List<CalendarEventDto> concreteEvents,
        List<CalendarEventDto> watchlistEvents,
        List<CalendarEventDto> deduplicated)
    {
        foreach (var wl in watchlistEvents)
        {
            var match = concreteEvents.FirstOrDefault(c => Math.Abs((c.ReleaseDate - wl.ReleaseDate).TotalDays) <= TimezoneToleranceDays);
            if (match != null)
            {
                match.IsWatchlisted = true;
            }
            else
            {
                deduplicated.Add(wl);
            }
        }
    }
}
