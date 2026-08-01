using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.LibraryMigration.ViewModels;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.LibraryMigration;

public interface ILibraryMigrationJobRunner
{
    LibraryMigrationJobVM StartJob(LibraryMigrationJobInput input);
    LibraryMigrationJobVM? GetJob(Guid jobId);
}

public class LibraryMigrationJobRunner : ILibraryMigrationJobRunner
{
    private static readonly TimeSpan CompletedJobRetention = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxJobAge = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LibraryMigrationJobRunner> _logger;
    private readonly ConcurrentDictionary<Guid, LibraryMigrationJobVM> _jobs = new();
    private readonly ConcurrentDictionary<Guid, object> _jobLocks = new();

    public LibraryMigrationJobRunner(IServiceScopeFactory scopeFactory, ILogger<LibraryMigrationJobRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public LibraryMigrationJobVM StartJob(LibraryMigrationJobInput input)
    {
        PruneStaleJobs();

        var jobId = Guid.NewGuid();
        var users = input.Mappings
            .Select(m => new LibraryMigrationUserStatusVM
            {
                AccountId = m.AccountId,
                AccountName = m.AccountName,
                ProfileId = m.ProfileId,
                ProfileName = m.ProfileName,
                State = LibraryMigrationUserState.Pending
            })
            .ToList();

        var job = new LibraryMigrationJobVM
        {
            JobId = jobId,
            ProviderId = input.ProviderId,
            ServerName = input.ServerName,
            State = LibraryMigrationJobState.Pending,
            StartedAt = DateTime.UtcNow,
            Users = users
        };

        _jobs[jobId] = job;
        _jobLocks[jobId] = new object();

        _ = Task.Run(async () =>
        {
            try
            {
                await RunJobAsync(jobId, input);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Library migration job {JobId} crashed.", jobId);
                UpdateJob(jobId, j =>
                {
                    j.State = LibraryMigrationJobState.Failed;
                    j.CompletedAt = DateTime.UtcNow;
                    j.ErrorMessage = ex.Message;
                });
            }
        });

        return Snapshot(job);
    }

    public LibraryMigrationJobVM? GetJob(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            var lockObj = _jobLocks.GetOrAdd(jobId, _ => new object());
            lock (lockObj)
            {
                return Snapshot(job);
            }
        }
        return null;
    }

    private async Task RunJobAsync(Guid jobId, LibraryMigrationJobInput input)
    {
        IClientNotifier notifier;
        bool providerExists;
        using (var initScope = _scopeFactory.CreateScope())
        {
            notifier = initScope.ServiceProvider.GetRequiredService<IClientNotifier>();
            var providers = initScope.ServiceProvider.GetRequiredService<IEnumerable<ILibrarySyncProvider>>();
            providerExists = providers.Any(p => p.Id.Equals(input.ProviderId, StringComparison.OrdinalIgnoreCase));
        }

        if (!providerExists)
        {
            UpdateJob(jobId, j =>
            {
                j.State = LibraryMigrationJobState.Failed;
                j.CompletedAt = DateTime.UtcNow;
                j.ErrorMessage = $"No provider registered with id '{input.ProviderId}'.";
            });
            await notifier.NotifyLibraryMigrationUpdatedAsync(GetSnapshot(jobId));
            return;
        }

        UpdateJob(jobId, j => j.State = LibraryMigrationJobState.Running);
        await notifier.NotifyLibraryMigrationUpdatedAsync(GetSnapshot(jobId));

        var scopeDto = new RemoteSyncScopeDto
        {
            IncludeWatchState = input.IncludeWatchState,
            IncludeRatings = input.IncludeRatings,
            LibrarySectionKeys = input.LibrarySectionKeys
        };

        foreach (var mapping in input.Mappings)
        {
            UpdateUser(jobId, mapping.AccountId, u => u.State = LibraryMigrationUserState.Running);
            await notifier.NotifyLibraryMigrationUpdatedAsync(GetSnapshot(jobId));

            try
            {
                using var userScope = _scopeFactory.CreateScope();
                var userProvider = userScope.ServiceProvider
                    .GetRequiredService<IEnumerable<ILibrarySyncProvider>>()
                    .First(p => p.Id.Equals(input.ProviderId, StringComparison.OrdinalIgnoreCase));

                await ProcessUserAsync(userScope.ServiceProvider, userProvider, input, scopeDto, mapping, jobId, notifier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Library migration job {JobId} failed for account {AccountId}.", jobId, mapping.AccountId);
                UpdateUser(jobId, mapping.AccountId, u =>
                {
                    u.State = LibraryMigrationUserState.Failed;
                    u.ErrorMessage = ex.Message;
                });
                await notifier.NotifyLibraryMigrationUpdatedAsync(GetSnapshot(jobId));
            }
        }

        UpdateJob(jobId, j =>
        {
            j.State = LibraryMigrationJobState.Completed;
            j.CompletedAt = DateTime.UtcNow;
        });
        await notifier.NotifyLibraryMigrationUpdatedAsync(GetSnapshot(jobId));
    }

    private async Task ProcessUserAsync(
        IServiceProvider scopedServices,
        ILibrarySyncProvider provider,
        LibraryMigrationJobInput input,
        RemoteSyncScopeDto scope,
        LibraryMigrationMappingInput mapping,
        Guid jobId,
        IClientNotifier notifier)
    {
        var repository = scopedServices.GetRequiredService<ILibraryMigrationRepository>();

        var userToken = input.SelfService
            ? input.AdminAccessToken
            : await provider.ResolveUserTokenAsync(input.AdminAccessToken, mapping.AccountId, mapping.Pin);
        var userData = await provider.FetchUserDataAsync(input.ConnectionUri, userToken, scope);

        UpdateUser(jobId, mapping.AccountId, u =>
        {
            u.WatchStatesFetched = userData.WatchStates.Count;
            u.RatingsFetched = userData.Ratings.Count;
        });

        var movieTmdbIds = CollectIds(userData, RemoteMediaKind.Movie, e => e.TmdbId);
        var movieImdbIds = CollectIds(userData, RemoteMediaKind.Movie, e => e.ImdbId);
        var movieTvdbIds = CollectIds(userData, RemoteMediaKind.Movie, e => e.TvdbId);

        var matchedItems = await repository.FindMatchesAsync(movieTmdbIds, movieImdbIds, movieTvdbIds);

        var tmdbMap = matchedItems
            .Where(m => !string.IsNullOrEmpty(m.TmdbId))
            .GroupBy(m => m.TmdbId!)
            .ToDictionary(g => g.Key, g => g.First().Id);
        var imdbMap = matchedItems
            .Where(m => !string.IsNullOrEmpty(m.ImdbId))
            .GroupBy(m => m.ImdbId!)
            .ToDictionary(g => g.Key, g => g.First().Id);
        var tvdbMap = matchedItems
            .Where(m => !string.IsNullOrEmpty(m.TvdbId))
            .GroupBy(m => m.TvdbId!)
            .ToDictionary(g => g.Key, g => g.First().Id);

        Guid? Match(RemoteExternalIdsDto ids)
        {
            if (!string.IsNullOrEmpty(ids.TmdbId) && tmdbMap.TryGetValue(ids.TmdbId, out var t)) return t;
            if (!string.IsNullOrEmpty(ids.ImdbId) && imdbMap.TryGetValue(ids.ImdbId, out var i)) return i;
            if (!string.IsNullOrEmpty(ids.TvdbId) && tvdbMap.TryGetValue(ids.TvdbId, out var v)) return v;
            return null;
        }

        var showTmdbIds = CollectIds(userData, RemoteMediaKind.Episode, e => e.TmdbId);
        var showImdbIds = CollectIds(userData, RemoteMediaKind.Episode, e => e.ImdbId);
        var showTvdbIds = CollectIds(userData, RemoteMediaKind.Episode, e => e.TvdbId);

        var episodeRows = await repository.FindEpisodeMatchesAsync(showTmdbIds, showImdbIds, showTvdbIds);
        var episodeMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in episodeRows)
        {
            if (!string.IsNullOrEmpty(row.ShowTmdbId)) episodeMap.TryAdd(EpisodeKey("tmdb", row.ShowTmdbId!, row.SeasonNumber, row.EpisodeNumber), row.Id);
            if (!string.IsNullOrEmpty(row.ShowImdbId)) episodeMap.TryAdd(EpisodeKey("imdb", row.ShowImdbId!, row.SeasonNumber, row.EpisodeNumber), row.Id);
            if (!string.IsNullOrEmpty(row.ShowTvdbId)) episodeMap.TryAdd(EpisodeKey("tvdb", row.ShowTvdbId!, row.SeasonNumber, row.EpisodeNumber), row.Id);
        }

        var epOwnTmdbIds = CollectEpisodeOwnIds(userData, e => e.TmdbId);
        var epOwnImdbIds = CollectEpisodeOwnIds(userData, e => e.ImdbId);
        var epOwnTvdbIds = CollectEpisodeOwnIds(userData, e => e.TvdbId);

        var episodeOwnRows = await repository.FindEpisodeMatchesByOwnIdsAsync(epOwnTmdbIds, epOwnImdbIds, epOwnTvdbIds);
        var epOwnTmdbMap = episodeOwnRows.Where(r => !string.IsNullOrEmpty(r.TmdbId)).GroupBy(r => r.TmdbId!).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var epOwnImdbMap = episodeOwnRows.Where(r => !string.IsNullOrEmpty(r.ImdbId)).GroupBy(r => r.ImdbId!).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var epOwnTvdbMap = episodeOwnRows.Where(r => !string.IsNullOrEmpty(r.TvdbId)).GroupBy(r => r.TvdbId!).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        Guid? MatchEpisode(RemoteExternalIdsDto showIds, RemoteExternalIdsDto? episodeIds, int? season, int? episode)
        {
            if (season is not null && episode is not null)
            {
                if (!string.IsNullOrEmpty(showIds.TmdbId) && episodeMap.TryGetValue(EpisodeKey("tmdb", showIds.TmdbId!, season.Value, episode.Value), out var t)) return t;
                if (!string.IsNullOrEmpty(showIds.ImdbId) && episodeMap.TryGetValue(EpisodeKey("imdb", showIds.ImdbId!, season.Value, episode.Value), out var i)) return i;
                if (!string.IsNullOrEmpty(showIds.TvdbId) && episodeMap.TryGetValue(EpisodeKey("tvdb", showIds.TvdbId!, season.Value, episode.Value), out var v)) return v;
            }
            if (episodeIds is not null)
            {
                if (!string.IsNullOrEmpty(episodeIds.TvdbId) && epOwnTvdbMap.TryGetValue(episodeIds.TvdbId!, out var ev)) return ev;
                if (!string.IsNullOrEmpty(episodeIds.ImdbId) && epOwnImdbMap.TryGetValue(episodeIds.ImdbId!, out var ei)) return ei;
                if (!string.IsNullOrEmpty(episodeIds.TmdbId) && epOwnTmdbMap.TryGetValue(episodeIds.TmdbId!, out var et)) return et;
            }
            return null;
        }

        Guid? MatchEntry(RemoteMediaKind kind, RemoteExternalIdsDto ids, RemoteExternalIdsDto? episodeIds, int? season, int? episode)
            => kind == RemoteMediaKind.Episode ? MatchEpisode(ids, episodeIds, season, episode) : Match(ids);

        var watchSkipped = 0;
        var ratingsSkipped = 0;
        var skippedSamples = new List<string>();
        var watchUpserts = new List<WatchStateUpsert>();
        var ratingUpserts = new List<RatingUpsert>();

        if (input.IncludeWatchState)
        {
            var matchedWatch = new List<(Guid MediaItemId, RemoteWatchStateDto State)>();
            foreach (var ws in userData.WatchStates)
            {
                var match = MatchEntry(ws.Kind, ws.ExternalIds, ws.EpisodeIds, ws.SeasonNumber, ws.EpisodeNumber);
                if (match.HasValue) matchedWatch.Add((match.Value, ws));
                else { watchSkipped++; RecordSkip(skippedSamples, "watch", ws.Kind, ws.ExternalIds, ws.EpisodeIds, ws.SeasonNumber, ws.EpisodeNumber); }
            }
            watchUpserts.AddRange(matchedWatch
                .GroupBy(p => p.MediaItemId)
                .Select(g => MergeWatchStates(g.Key, g.Select(x => x.State))));
            await repository.BulkUpsertWatchStatesAsync(mapping.ProfileId, watchUpserts);
        }

        if (input.IncludeRatings)
        {
            var matchedRatings = new List<(Guid MediaItemId, RemoteRatingDto Rating)>();
            foreach (var r in userData.Ratings)
            {
                var match = MatchEntry(r.Kind, r.ExternalIds, r.EpisodeIds, r.SeasonNumber, r.EpisodeNumber);
                if (match.HasValue) matchedRatings.Add((match.Value, r));
                else { ratingsSkipped++; RecordSkip(skippedSamples, "rating", r.Kind, r.ExternalIds, r.EpisodeIds, r.SeasonNumber, r.EpisodeNumber); }
            }
            ratingUpserts.AddRange(matchedRatings
                .GroupBy(p => p.MediaItemId)
                .Select(g => MergeRatings(g.Key, g.Select(x => x.Rating))));
            await repository.BulkUpsertRatingsAsync(mapping.ProfileId, ratingUpserts);
            if (input.SetAdminRatings)
            {
                await repository.BulkSetAdminRatingsAsync(ratingUpserts);
            }
        }

        if (watchSkipped + ratingsSkipped > 0)
        {
            _logger.LogInformation(
                "Library import job {JobId}: {SkipCount} entr(ies) had no Vora match. Sample: {Samples}",
                jobId, watchSkipped + ratingsSkipped, string.Join(" | ", skippedSamples));
        }

        UpdateUser(jobId, mapping.AccountId, u =>
        {
            u.State = LibraryMigrationUserState.Completed;
            u.WatchStatesImported = watchUpserts.Count;
            u.RatingsImported = ratingUpserts.Count;
            u.Skipped = watchSkipped + ratingsSkipped;
            u.SkippedSamples = skippedSamples;
        });
        await notifier.NotifyLibraryMigrationUpdatedAsync(GetSnapshot(jobId));
    }

    private static WatchStateUpsert MergeWatchStates(Guid mediaItemId, IEnumerable<RemoteWatchStateDto> entries)
    {
        var isPlayed = false;
        DateTime? bestLastPlayed = null;
        double bestResume = 0.0;
        var bestResumeFallback = 0.0;

        foreach (var entry in entries)
        {
            if (entry.IsPlayed) isPlayed = true;
            if (entry.ResumePositionSeconds > bestResumeFallback)
            {
                bestResumeFallback = entry.ResumePositionSeconds;
            }
            if (entry.LastPlayedAt.HasValue)
            {
                if (bestLastPlayed is null || entry.LastPlayedAt.Value > bestLastPlayed.Value)
                {
                    bestLastPlayed = entry.LastPlayedAt;
                    bestResume = entry.ResumePositionSeconds;
                }
            }
        }

        return new WatchStateUpsert
        {
            MediaItemId = mediaItemId,
            IsPlayed = isPlayed,
            ResumePositionSeconds = bestLastPlayed.HasValue ? bestResume : bestResumeFallback,
            LastPlayedAt = bestLastPlayed
        };
    }

    private static RatingUpsert MergeRatings(Guid mediaItemId, IEnumerable<RemoteRatingDto> entries)
    {
        decimal bestRating = 0m;
        DateTime? bestRatedAt = null;
        var anyEntry = false;

        foreach (var entry in entries)
        {
            anyEntry = true;
            if (entry.RatedAt.HasValue)
            {
                if (bestRatedAt is null || entry.RatedAt.Value > bestRatedAt.Value)
                {
                    bestRatedAt = entry.RatedAt;
                    bestRating = entry.Rating;
                }
            }
            else if (bestRatedAt is null && entry.Rating > bestRating)
            {
                bestRating = entry.Rating;
            }
        }

        if (!anyEntry)
        {
            throw new InvalidOperationException("MergeRatings called with no entries.");
        }

        return new RatingUpsert
        {
            MediaItemId = mediaItemId,
            Rating = bestRating,
            RatedAt = bestRatedAt
        };
    }

    private static string EpisodeKey(string idType, string id, int season, int episode) => $"{idType}:{id}:{season}:{episode}";

    private static List<string> CollectEpisodeOwnIds(RemoteUserDataDto data, Func<RemoteExternalIdsDto, string?> selector)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ws in data.WatchStates)
        {
            if (ws.Kind != RemoteMediaKind.Episode || ws.EpisodeIds is null) continue;
            var id = selector(ws.EpisodeIds);
            if (!string.IsNullOrEmpty(id)) set.Add(id);
        }
        foreach (var r in data.Ratings)
        {
            if (r.Kind != RemoteMediaKind.Episode || r.EpisodeIds is null) continue;
            var id = selector(r.EpisodeIds);
            if (!string.IsNullOrEmpty(id)) set.Add(id);
        }
        return set.ToList();
    }

    private static void RecordSkip(List<string> samples, string kindLabel, RemoteMediaKind kind, RemoteExternalIdsDto ids, RemoteExternalIdsDto? episodeIds, int? season, int? episode)
    {
        if (samples.Count >= 40) return;
        var idPart = !string.IsNullOrEmpty(ids.TmdbId) ? $"tmdb:{ids.TmdbId}"
            : !string.IsNullOrEmpty(ids.ImdbId) ? $"imdb:{ids.ImdbId}"
            : !string.IsNullOrEmpty(ids.TvdbId) ? $"tvdb:{ids.TvdbId}"
            : "no-id";
        if (kind == RemoteMediaKind.Episode)
        {
            var epPart = episodeIds is null ? "ep:none"
                : !string.IsNullOrEmpty(episodeIds.TvdbId) ? $"ep-tvdb:{episodeIds.TvdbId}"
                : !string.IsNullOrEmpty(episodeIds.ImdbId) ? $"ep-imdb:{episodeIds.ImdbId}"
                : !string.IsNullOrEmpty(episodeIds.TmdbId) ? $"ep-tmdb:{episodeIds.TmdbId}"
                : "ep:none";
            samples.Add($"{kindLabel} episode {idPart} S{season}E{episode} {epPart}");
        }
        else
        {
            samples.Add($"{kindLabel} movie {idPart}");
        }
    }

    private static List<string> CollectIds(RemoteUserDataDto data, RemoteMediaKind kind, Func<RemoteExternalIdsDto, string?> selector)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ws in data.WatchStates)
        {
            if (ws.Kind != kind) continue;
            var id = selector(ws.ExternalIds);
            if (!string.IsNullOrEmpty(id)) set.Add(id);
        }
        foreach (var r in data.Ratings)
        {
            if (r.Kind != kind) continue;
            var id = selector(r.ExternalIds);
            if (!string.IsNullOrEmpty(id)) set.Add(id);
        }
        return set.ToList();
    }

    private void PruneStaleJobs()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _jobs)
        {
            var job = kvp.Value;
            var terminal = job.State is LibraryMigrationJobState.Completed or LibraryMigrationJobState.Failed;
            var retentionExpired = terminal && job.CompletedAt.HasValue && now - job.CompletedAt.Value > CompletedJobRetention;
            var exceededMaxAge = now - job.StartedAt > MaxJobAge;

            if (retentionExpired || exceededMaxAge)
            {
                _jobs.TryRemove(kvp.Key, out _);
                _jobLocks.TryRemove(kvp.Key, out _);
            }
        }
    }

    private LibraryMigrationJobVM GetSnapshot(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            var lockObj = _jobLocks.GetOrAdd(jobId, _ => new object());
            lock (lockObj)
            {
                return Snapshot(job);
            }
        }
        throw new InvalidOperationException($"Job {jobId} not found.");
    }

    private void UpdateJob(Guid jobId, Action<LibraryMigrationJobVM> mutator)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return;
        var lockObj = _jobLocks.GetOrAdd(jobId, _ => new object());
        lock (lockObj)
        {
            mutator(job);
        }
    }

    private void UpdateUser(Guid jobId, string accountId, Action<LibraryMigrationUserStatusVM> mutator)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return;
        var lockObj = _jobLocks.GetOrAdd(jobId, _ => new object());
        lock (lockObj)
        {
            var user = job.Users.FirstOrDefault(u => string.Equals(u.AccountId, accountId, StringComparison.Ordinal));
            if (user is not null) mutator(user);
        }
    }

    private static LibraryMigrationJobVM Snapshot(LibraryMigrationJobVM job)
    {
        return new LibraryMigrationJobVM
        {
            JobId = job.JobId,
            ProviderId = job.ProviderId,
            ServerName = job.ServerName,
            State = job.State,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            ErrorMessage = job.ErrorMessage,
            Users = job.Users.Select(u => new LibraryMigrationUserStatusVM
            {
                AccountId = u.AccountId,
                AccountName = u.AccountName,
                ProfileId = u.ProfileId,
                ProfileName = u.ProfileName,
                State = u.State,
                WatchStatesFetched = u.WatchStatesFetched,
                WatchStatesImported = u.WatchStatesImported,
                RatingsFetched = u.RatingsFetched,
                RatingsImported = u.RatingsImported,
                Skipped = u.Skipped,
                SkippedSamples = u.SkippedSamples.ToList(),
                ErrorMessage = u.ErrorMessage
            }).ToList()
        };
    }
}
