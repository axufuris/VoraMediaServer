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

        var tmdbIds = CollectIds(userData, e => e.TmdbId);
        var imdbIds = CollectIds(userData, e => e.ImdbId);
        var tvdbIds = CollectIds(userData, e => e.TvdbId);

        var matchedItems = await repository.FindMatchesAsync(tmdbIds, imdbIds, tvdbIds);

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

        var watchSkipped = 0;
        var ratingsSkipped = 0;
        var watchUpserts = new List<WatchStateUpsert>();
        var ratingUpserts = new List<RatingUpsert>();

        if (input.IncludeWatchState)
        {
            var matchedWatch = new List<(Guid MediaItemId, RemoteWatchStateDto State)>();
            foreach (var ws in userData.WatchStates)
            {
                var match = Match(ws.ExternalIds);
                if (match.HasValue) matchedWatch.Add((match.Value, ws));
                else watchSkipped++;
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
                var match = Match(r.ExternalIds);
                if (match.HasValue) matchedRatings.Add((match.Value, r));
                else ratingsSkipped++;
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

        UpdateUser(jobId, mapping.AccountId, u =>
        {
            u.State = LibraryMigrationUserState.Completed;
            u.WatchStatesImported = watchUpserts.Count;
            u.RatingsImported = ratingUpserts.Count;
            u.Skipped = watchSkipped + ratingsSkipped;
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

    private static List<string> CollectIds(RemoteUserDataDto data, Func<RemoteExternalIdsDto, string?> selector)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ws in data.WatchStates)
        {
            var id = selector(ws.ExternalIds);
            if (!string.IsNullOrEmpty(id)) set.Add(id);
        }
        foreach (var r in data.Ratings)
        {
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
                ErrorMessage = u.ErrorMessage
            }).ToList()
        };
    }
}
