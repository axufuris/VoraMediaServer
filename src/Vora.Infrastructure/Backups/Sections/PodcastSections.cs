using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Podcasts;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups.Sections;

public sealed class PodcastShowsBackupSection : IBackupSection
{
    private readonly VoraDbContext _db;

    public PodcastShowsBackupSection(VoraDbContext db)
    {
        _db = db;
    }

    public string Key => "podcasts.shows";
    public string DisplayName => "Podcast Catalog";
    public BackupSectionGroup Group => BackupSectionGroup.Podcasts;
    public bool RequiresExplicitConfirm => true;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning =>
        "Replaces the podcast catalog and every show a profile follows. Shows that are not in the backup are removed with their subscriptions and listening progress. Episodes are fetched again from each feed.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        await writer.WriteJsonAsync($"{Key}/shows.json", await _db.PodcastShows.AsNoTracking().ToListAsync(ct), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var shows = await reader.ReadJsonAsync<List<PodcastShow>>($"{Key}/shows.json", ct);
        if (shows == null) return new BackupSectionImportResult();

        var tally = new BackupSkipTally();
        var unique = shows.GroupBy(s => s.FeedUrl, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        tally.Skip(new BackupRowNoun("podcast", "podcasts"), BackupSkipReason.Duplicate, shows.Count - unique.Count);

        var existing = await _db.PodcastShows.ToListAsync(ct);
        var existingByFeed = existing.ToDictionary(s => s.FeedUrl, StringComparer.OrdinalIgnoreCase);
        var incomingFeeds = unique.Select(s => s.FeedUrl).ToHashSet(StringComparer.OrdinalIgnoreCase);

        _db.PodcastShows.RemoveRange(existing.Where(s => !incomingFeeds.Contains(s.FeedUrl)));
        await _db.SaveChangesAsync(ct);

        var takenIds = existing.Select(s => s.Id).ToHashSet();
        foreach (var show in unique)
        {
            if (existingByFeed.TryGetValue(show.FeedUrl, out var current))
            {
                current.Title = show.Title;
                current.Author = show.Author;
                current.Description = show.Description;
                current.ArtworkUrl = show.ArtworkUrl;
                current.HomepageUrl = show.HomepageUrl;
                current.Language = show.Language;
                current.AddedAt = show.AddedAt;
                current.IsInCatalog = show.IsInCatalog;
                continue;
            }

            _db.PodcastShows.Add(new PodcastShow
            {
                Id = takenIds.Contains(show.Id) ? Guid.NewGuid() : show.Id,
                FeedUrl = show.FeedUrl,
                Title = show.Title,
                Author = show.Author,
                Description = show.Description,
                ArtworkUrl = show.ArtworkUrl,
                HomepageUrl = show.HomepageUrl,
                Language = show.Language,
                AddedAt = show.AddedAt,
                IsInCatalog = show.IsInCatalog
            });
        }

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();

        return tally.ToResult(unique.Count);
    }
}

public sealed class PodcastListeningBackupSection : IBackupSection
{
    private static readonly BackupRowNoun SubscriptionNoun = new("podcast subscription", "podcast subscriptions");
    private static readonly BackupRowNoun ProgressNoun = new("episode progress row", "episode progress rows");

    private readonly VoraDbContext _db;

    public PodcastListeningBackupSection(VoraDbContext db)
    {
        _db = db;
    }

    public string Key => "podcasts.listening";
    public string DisplayName => "Podcast Subscriptions & Progress";
    public BackupSectionGroup Group => BackupSectionGroup.Podcasts;
    public bool RequiresExplicitConfirm => true;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning => "Replaces every profile's podcast subscriptions and episode listening progress.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        var subscriptions = await _db.PodcastSubscriptions.AsNoTracking().ToListAsync(ct);
        var progress = await _db.PodcastEpisodeProfileStates.AsNoTracking().ToListAsync(ct);

        var showIds = subscriptions.Select(s => s.PodcastShowId).ToHashSet();
        var episodeIds = progress.Select(p => p.PodcastEpisodeId).Distinct().ToList();
        var episodes = await _db.PodcastEpisodes.AsNoTracking()
            .Where(e => episodeIds.Contains(e.Id))
            .Select(e => new PodcastEpisodeIdentity
            {
                Id = e.Id,
                FeedUrl = e.Show.FeedUrl,
                ExternalGuid = e.ExternalGuid,
                Title = e.Title,
                AudioUrl = e.AudioUrl,
                ArtworkUrl = e.ArtworkUrl,
                DurationSeconds = e.DurationSeconds,
                PublishedAt = e.PublishedAt,
                EpisodeNumber = e.EpisodeNumber,
                SeasonNumber = e.SeasonNumber
            })
            .ToListAsync(ct);
        showIds.UnionWith(await _db.PodcastEpisodes.Where(e => episodeIds.Contains(e.Id)).Select(e => e.PodcastShowId).ToListAsync(ct));
        var shows = await _db.PodcastShows.AsNoTracking()
            .Where(s => showIds.Contains(s.Id))
            .Select(s => new PodcastShowIdentity { Id = s.Id, FeedUrl = s.FeedUrl })
            .ToListAsync(ct);

        await writer.WriteJsonAsync($"{Key}/subscriptions.json", subscriptions, ct);
        await writer.WriteJsonAsync($"{Key}/episode-progress.json", progress, ct);
        await writer.WriteJsonAsync($"{Key}/show-{BackupJson.IdentitiesFileSuffix}", shows, ct);
        await writer.WriteJsonAsync($"{Key}/episode-{BackupJson.IdentitiesFileSuffix}", episodes, ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var subscriptions = await reader.ReadJsonAsync<List<PodcastSubscription>>($"{Key}/subscriptions.json", ct);
        var progress = await reader.ReadJsonAsync<List<PodcastEpisodeProfileState>>($"{Key}/episode-progress.json", ct);
        if (subscriptions == null && progress == null) return new BackupSectionImportResult();

        var showIdentities = await reader.ReadJsonAsync<List<PodcastShowIdentity>>($"{Key}/show-{BackupJson.IdentitiesFileSuffix}", ct) ?? new();
        var episodeIdentities = await reader.ReadJsonAsync<List<PodcastEpisodeIdentity>>($"{Key}/episode-{BackupJson.IdentitiesFileSuffix}", ct) ?? new();

        var tally = new BackupSkipTally();
        var profileIds = await _db.UserProfiles.Select(p => p.Id).ToHashSetAsync(ct);
        var shows = await _db.PodcastShows.AsNoTracking().Select(s => new { s.Id, s.FeedUrl }).ToListAsync(ct);
        var showIds = shows.Select(s => s.Id).ToHashSet();
        var showsByFeed = shows.GroupBy(s => s.FeedUrl, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var recordedFeeds = showIdentities.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().FeedUrl);
        var imported = 0;

        Guid? ResolveShow(Guid id)
        {
            if (showIds.Contains(id)) return id;
            return recordedFeeds.TryGetValue(id, out var feed) && showsByFeed.TryGetValue(feed, out var current) ? current : null;
        }

        if (subscriptions != null)
        {
            var restorable = new List<PodcastSubscription>();
            foreach (var subscription in subscriptions)
            {
                if (!profileIds.Contains(subscription.ProfileId)) { tally.Skip(SubscriptionNoun, BackupSkipReason.MissingProfile); continue; }
                var showId = ResolveShow(subscription.PodcastShowId);
                if (showId == null) { tally.Skip(SubscriptionNoun, BackupSkipReason.MissingPodcast); continue; }
                subscription.PodcastShowId = showId.Value;
                restorable.Add(subscription);
            }
            restorable = BackupTableSync.KeepOnePer(restorable, s => (s.ProfileId, s.PodcastShowId), s => s.SubscribedAt, tally, SubscriptionNoun);
            await BackupTableSync.ReplaceAsync(_db, _db.PodcastSubscriptions, restorable, ct);
            imported += restorable.Count;
        }

        if (progress != null)
        {
            var episodeMap = await ResolveEpisodesAsync(progress.Select(p => p.PodcastEpisodeId).ToHashSet(), episodeIdentities, showsByFeed, ct);
            var restorable = new List<PodcastEpisodeProfileState>();
            foreach (var state in progress)
            {
                if (!profileIds.Contains(state.ProfileId)) { tally.Skip(ProgressNoun, BackupSkipReason.MissingProfile); continue; }
                if (!episodeMap.TryGetValue(state.PodcastEpisodeId, out var episodeId)) { tally.Skip(ProgressNoun, BackupSkipReason.MissingPodcast); continue; }
                state.PodcastEpisodeId = episodeId;
                restorable.Add(state);
            }
            restorable = BackupTableSync.KeepOnePer(restorable, s => (s.ProfileId, s.PodcastEpisodeId), s => s.LastListenedAt, tally, ProgressNoun);
            await BackupTableSync.ReplaceAsync(_db, _db.PodcastEpisodeProfileStates, restorable, ct);
            imported += restorable.Count;
        }

        return tally.ToResult(imported);
    }

    private async Task<Dictionary<Guid, Guid>> ResolveEpisodesAsync(
        HashSet<Guid> referencedIds,
        List<PodcastEpisodeIdentity> identities,
        Dictionary<string, Guid> showsByFeed,
        CancellationToken ct)
    {
        var referenced = referencedIds.ToList();
        var present = await _db.PodcastEpisodes.Where(e => referenced.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct);
        var map = present.ToDictionary(id => id, id => id);

        var missing = identities
            .Where(i => referencedIds.Contains(i.Id) && !map.ContainsKey(i.Id) && showsByFeed.ContainsKey(i.FeedUrl))
            .GroupBy(i => i.Id)
            .Select(g => g.First())
            .ToList();
        if (missing.Count == 0) return map;

        var showIds = missing.Select(i => showsByFeed[i.FeedUrl]).Distinct().ToList();
        var candidates = await _db.PodcastEpisodes.AsNoTracking()
            .Where(e => showIds.Contains(e.PodcastShowId))
            .Select(e => new { e.Id, e.PodcastShowId, e.ExternalGuid, e.AudioUrl })
            .ToListAsync(ct);
        var byGuid = candidates.GroupBy(c => (c.PodcastShowId, c.ExternalGuid)).ToDictionary(g => g.Key, g => g.First().Id);
        var byAudio = candidates.GroupBy(c => (c.PodcastShowId, c.AudioUrl)).ToDictionary(g => g.Key, g => g.First().Id);

        foreach (var identity in missing)
        {
            var showId = showsByFeed[identity.FeedUrl];
            if (byGuid.TryGetValue((showId, identity.ExternalGuid), out var matchedId) || byAudio.TryGetValue((showId, identity.AudioUrl), out matchedId))
            {
                map[identity.Id] = matchedId;
                continue;
            }

            var placeholder = new PodcastEpisode
            {
                PodcastShowId = showId,
                ExternalGuid = identity.ExternalGuid,
                Title = identity.Title,
                AudioUrl = identity.AudioUrl,
                ArtworkUrl = identity.ArtworkUrl,
                DurationSeconds = identity.DurationSeconds,
                PublishedAt = identity.PublishedAt,
                EpisodeNumber = identity.EpisodeNumber,
                SeasonNumber = identity.SeasonNumber
            };
            _db.PodcastEpisodes.Add(placeholder);
            byGuid[(showId, identity.ExternalGuid)] = placeholder.Id;
            map[identity.Id] = placeholder.Id;
        }

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return map;
    }
}

public sealed class PodcastShowIdentity
{
    public Guid Id { get; set; }
    public string FeedUrl { get; set; } = string.Empty;
}

public sealed class PodcastEpisodeIdentity
{
    public Guid Id { get; set; }
    public string FeedUrl { get; set; } = string.Empty;
    public string ExternalGuid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string? ArtworkUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? SeasonNumber { get; set; }
}
