using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Media.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media;

public interface IMusicRecommendationManager
{
    Task<List<GeneratedMixSummaryVM>> GetMixesForProfileAsync(Guid profileId, MusicAccessFilter access);
    Task<GeneratedMixDetailVM?> GetMixDetailAsync(Guid mixId, Guid profileId, MusicAccessFilter access);
    Task<List<BecauseYouPlayedRowVM>> GetBecauseYouPlayedRowsAsync(Guid profileId, MusicAccessFilter access);

    Task RefreshMixesForProfileAsync(Guid profileId);
    Task RefreshAllActiveProfilesAsync(CancellationToken cancellationToken);

    Task<RadioQueueVM> StartRadioAsync(Guid profileId, MusicAccessFilter access, RadioSeed seed, int size);
    Task<RadioQueueVM> ExtendRadioAsync(Guid profileId, MusicAccessFilter access, RadioSeed seed, IEnumerable<Guid> excludeTrackIds, int size);

    Task<List<StationVM>> GetStationsForProfileAsync(Guid profileId);
    Task<StationVM?> SaveStationAsync(Guid profileId, MusicAccessFilter access, string name, RadioSeed seed);
    Task DeleteStationAsync(Guid profileId, Guid stationId);
    Task TouchStationLastPlayedAsync(Guid profileId, Guid stationId);

    Task<YearRecapVM> GetYearRecapAsync(Guid profileId, MusicAccessFilter access, int year);
    Task<List<int>> GetYearsWithHistoryAsync(Guid profileId);

    Task<List<ArtistVM>> GetSimilarArtistsAsync(Guid artistId, MusicAccessFilter access, CancellationToken cancellationToken);
    Task<List<string>> GetArtistTagsAsync(Guid artistId, CancellationToken cancellationToken);

    Task RefreshWeeklyMixesForProfileAsync(Guid profileId, CancellationToken cancellationToken);
    Task RefreshWeeklyMixesForAllAsync(CancellationToken cancellationToken);
}

public class YearRecapVM
{
    public int Year { get; set; }
    public int TotalPlays { get; set; }
    public long TotalListeningSeconds { get; set; }
    public int DistinctTrackCount { get; set; }
    public int DistinctArtistCount { get; set; }
    public int DistinctAlbumCount { get; set; }
    public List<YearRecapTrackVM> TopTracks { get; set; } = new();
    public List<YearRecapArtistVM> TopArtists { get; set; } = new();
    public List<YearRecapGenreVM> TopGenres { get; set; } = new();
    public List<int> PlaysByDayOfWeek { get; set; } = new();
    public List<int> PlaysByHour { get; set; } = new();
    public string? PeakDayOfWeekLabel { get; set; }
    public string? PeakHourLabel { get; set; }
    public List<YearRecapArtistVM> NewDiscoveries { get; set; } = new();
}

public class YearRecapTrackVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? AlbumTitle { get; set; }
    public string? AlbumArtworkUrl { get; set; }
    public int PlayCount { get; set; }
}

public class YearRecapArtistVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ArtworkUrl { get; set; }
    public int PlayCount { get; set; }
}

public class YearRecapGenreVM
{
    public string Name { get; set; } = string.Empty;
    public int PlayCount { get; set; }
    public int Percent { get; set; }
}

public sealed class RadioSeed
{
    public required StationSeedKind Kind { get; init; }
    public Guid? ArtistId { get; init; }
    public Guid? TrackId { get; init; }
    public string? Genre { get; init; }
}

public class RadioQueueVM
{
    public required string SeedKind { get; init; }
    public Guid? SeedArtistId { get; init; }
    public Guid? SeedTrackId { get; init; }
    public string? SeedGenre { get; init; }
    public required string SeedLabel { get; init; }
    public List<TrackVM> Tracks { get; set; } = new();
}

public class StationVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SeedKind { get; set; } = string.Empty;
    public Guid? SeedArtistId { get; set; }
    public Guid? SeedTrackId { get; set; }
    public string? SeedGenre { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? SubtitleHint { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastPlayedAt { get; set; }
}

public class MusicRecommendationManager : IMusicRecommendationManager
{
    private const int TopArtistsWindowDays = 90;
    private const int TopArtistsLimit = 50;
    private const int BecauseYouPlayedWindowDays = 7;
    private const int RecentDriftWindowDays = 7;

    private readonly IMusicRecommendationRepository _repo;
    private readonly IMusicRepository _musicRepo;
    private readonly IUserRepository _userRepo;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IClientNotifier _notifier;
    private readonly IEnumerable<IListeningDataProvider> _listeningProviders;
    private readonly ILogger<MusicRecommendationManager> _logger;

    public MusicRecommendationManager(
        IMusicRecommendationRepository repo,
        IMusicRepository musicRepo,
        IUserRepository userRepo,
        ISystemSettingsRepository settingsRepo,
        IClientNotifier notifier,
        IEnumerable<IListeningDataProvider> listeningProviders,
        ILogger<MusicRecommendationManager> logger)
    {
        _repo = repo;
        _musicRepo = musicRepo;
        _userRepo = userRepo;
        _settingsRepo = settingsRepo;
        _notifier = notifier;
        _listeningProviders = listeningProviders;
        _logger = logger;
    }

    public async Task<List<GeneratedMixSummaryVM>> GetMixesForProfileAsync(Guid profileId, MusicAccessFilter access)
    {
        var daily = await _repo.GetMixesForProfileAsync(profileId, GeneratedMixKind.DailyMix);
        var discover = await _repo.GetMixesForProfileAsync(profileId, GeneratedMixKind.DiscoverMix);
        var mood = await _repo.GetMixesForProfileAsync(profileId, GeneratedMixKind.MoodMix);
        return daily.Concat(discover).Concat(mood).Select(MapSummary).ToList();
    }

    public async Task<GeneratedMixDetailVM?> GetMixDetailAsync(Guid mixId, Guid profileId, MusicAccessFilter access)
    {
        var mix = await _repo.GetMixByIdAsync(mixId, profileId);
        if (mix == null) return null;

        var tracks = await _repo.GetTracksByIdsAsync(mix.TrackOrder, access);
        var likedIds = await GetLikedIdsAsync(profileId, tracks);

        var trackVms = tracks.Select(t => new TrackVM
        {
            Id = t.Id,
            Title = t.Title,
            SortTitle = t.SortTitle,
            Artist = t.Artist,
            TrackNumber = t.TrackNumber,
            DiscNumber = t.DiscNumber,
            DurationSeconds = t.DurationSeconds,
            ContentRating = t.ContentRating,
            AlbumId = t.AlbumId,
            IsLiked = likedIds.Contains(t.Id),
            LockedFields = t.LockedFields ?? new List<string>()
        }).ToList();

        return new GeneratedMixDetailVM
        {
            Id = mix.Id,
            Slot = mix.Slot,
            Name = mix.Name,
            DescriptionTag = mix.DescriptionTag,
            ArtworkUrl = mix.ArtworkUrl,
            GeneratedAt = mix.GeneratedAt,
            LastDriftAt = mix.LastDriftAt,
            Tracks = trackVms
        };
    }

    public async Task<List<BecauseYouPlayedRowVM>> GetBecauseYouPlayedRowsAsync(Guid profileId, MusicAccessFilter access)
    {
        var topArtists = await _repo.GetTopArtistsForProfileAsync(profileId, access, BecauseYouPlayedWindowDays, 3);
        if (topArtists.Count == 0) return new List<BecauseYouPlayedRowVM>();

        var artistIds = topArtists.Select(a => a.ArtistId).ToList();
        var genresByArtist = await _repo.GetGenresForArtistsAsync(artistIds);

        var rows = new List<BecauseYouPlayedRowVM>();
        foreach (var seedArtist in topArtists)
        {
            var seedTracks = await _repo.GetTopTracksByArtistAsync(seedArtist.ArtistId, access, profileId, limit: 6, maxPerAlbum: 2);
            if (seedTracks.Count == 0) continue;

            var rowTracks = seedTracks.ToList();
            rows.Add(new BecauseYouPlayedRowVM
            {
                Heading = $"Because you played {seedArtist.ArtistName}",
                SeedArtistId = seedArtist.ArtistId,
                Tracks = rowTracks.Select(MapArtistTrack).ToList()
            });
        }
        return rows;
    }

    public async Task RefreshMixesForProfileAsync(Guid profileId)
    {
        var profile = await _userRepo.GetProfileByIdAsync(profileId);
        if (profile == null) return;
        var settings = await _settingsRepo.GetSettingsAsync();
        if (!settings.EnableDailyMixes) return;

        var access = BuildAccessFilter(profile);

        var topArtists = await _repo.GetTopArtistsForProfileAsync(profileId, access, TopArtistsWindowDays, TopArtistsLimit);
        if (topArtists.Count < 3 || profileHasFewPlaysAsync(topArtists, settings.DailyMixMinPlays))
        {
            _logger.LogDebug("Profile {ProfileId} has insufficient plays for daily mix generation", profileId);
            return;
        }

        var genresByArtist = await _repo.GetGenresForArtistsAsync(topArtists.Select(a => a.ArtistId));
        var clusters = ClusterByGenre(topArtists, genresByArtist);
        var topClusters = clusters
            .OrderByDescending(c => c.TotalScore)
            .Take(settings.DailyMixCount)
            .ToList();

        if (topClusters.Count == 0) return;

        var newMixes = new List<GeneratedMix>();
        for (int i = 0; i < topClusters.Count; i++)
        {
            var cluster = topClusters[i];
            var trackIds = await BuildMixTrackIdsAsync(cluster, profileId, access, settings.DailyMixSize);
            if (trackIds.Count == 0) continue;

            var tracks = await _repo.GetTracksByIdsAsync(trackIds, access);
            var artwork = tracks.FirstOrDefault(t => t.Album != null && !string.IsNullOrWhiteSpace(t.Album.ArtworkUrl))?.Album?.ArtworkUrl;

            var name = string.IsNullOrWhiteSpace(cluster.DominantTag)
                ? $"Daily Mix {i + 1}"
                : $"Daily Mix {i + 1} · {cluster.DominantTag}";

            newMixes.Add(new GeneratedMix
            {
                ProfileId = profileId,
                Slot = i + 1,
                Name = name,
                DescriptionTag = cluster.DominantTag,
                Kind = GeneratedMixKind.DailyMix,
                ArtworkUrl = artwork,
                GeneratedAt = DateTime.UtcNow,
                TrackOrder = trackIds
            });
        }

        var existingMixes = await _repo.GetMixesForProfileAsync(profileId, GeneratedMixKind.DailyMix);

        foreach (var newMix in newMixes)
        {
            var existing = existingMixes.FirstOrDefault(m => m.Slot == newMix.Slot);
            if (existing == null)
            {
                await _repo.SaveMixAsync(newMix);
                continue;
            }

            var blended = DriftBlend(existing.TrackOrder, newMix.TrackOrder, settings.DailyMixDriftPercent);
            existing.Name = newMix.Name;
            existing.DescriptionTag = newMix.DescriptionTag;
            existing.ArtworkUrl = newMix.ArtworkUrl;
            existing.TrackOrder = blended;
            await _repo.SaveMixAsync(existing);
        }

        var orphanedSlots = existingMixes
            .Where(e => !newMixes.Any(n => n.Slot == e.Slot))
            .ToList();
        foreach (var orphan in orphanedSlots)
        {
            await _repo.DeleteMixesForProfileAsync(profileId, GeneratedMixKind.DailyMix);
            foreach (var m in newMixes) await _repo.SaveMixAsync(m);
            break;
        }

        await _notifier.NotifyMusicMixesUpdatedAsync(profileId);
    }

    public async Task RefreshAllActiveProfilesAsync(CancellationToken cancellationToken)
    {
        var profileIds = await _repo.GetProfileIdsWithRecentActivityAsync(14);
        foreach (var pid in profileIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RefreshMixesForProfileAsync(pid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mix refresh failed for profile {ProfileId}", pid);
            }
        }

        var settings = await _settingsRepo.GetSettingsForUpdateAsync();
        settings.DailyMixLastRefreshedAt = DateTime.UtcNow;
        await _settingsRepo.SaveChangesAsync();
    }

    private async Task<HashSet<Guid>> GetLikedIdsAsync(Guid profileId, List<Track> tracks)
    {
        if (tracks.Count == 0) return new HashSet<Guid>();
        return await _musicRepo.GetLikedTrackIdsAsync(profileId, tracks.Select(t => t.Id));
    }

    private static bool profileHasFewPlaysAsync(List<ArtistPlayScore> topArtists, int minPlays)
    {
        return topArtists.Sum(a => a.Score) < minPlays;
    }

    private async Task<List<Guid>> BuildMixTrackIdsAsync(GenreCluster cluster, Guid profileId, MusicAccessFilter access, int targetSize)
    {
        var headCount = Math.Max(1, targetSize * 4 / 10);
        var midCount = Math.Max(1, targetSize * 4 / 10);
        var likedCount = Math.Max(0, targetSize - headCount - midCount);

        var head = cluster.Members.OrderByDescending(m => m.Score).Take(5).ToList();
        var midTail = cluster.Members.OrderByDescending(m => m.Score).Skip(5).Take(15).ToList();

        var candidates = new List<Track>();

        foreach (var artist in head)
        {
            var tracks = await _repo.GetTopTracksByArtistAsync(artist.ArtistId, access, profileId, 4, 2);
            candidates.AddRange(tracks);
            if (candidates.Count >= headCount) break;
        }

        foreach (var artist in midTail)
        {
            var tracks = await _repo.GetTopTracksByArtistAsync(artist.ArtistId, access, profileId, 2, 2);
            candidates.AddRange(tracks);
            if (candidates.Count >= headCount + midCount) break;
        }

        if (likedCount > 0 && cluster.AllGenres.Count > 0)
        {
            var liked = await _repo.GetLikedTracksByGenreAsync(profileId, cluster.AllGenres, access, likedCount * 2);
            var existingIds = candidates.Select(c => c.Id).ToHashSet();
            foreach (var t in liked)
            {
                if (existingIds.Add(t.Id))
                {
                    candidates.Add(t);
                    if (candidates.Count >= targetSize) break;
                }
            }
        }

        candidates = DedupeById(candidates);
        var interleaved = InterleaveForVariety(candidates, maxConsecutiveSameArtist: 1, maxPerArtist: 3);
        return interleaved.Take(targetSize).Select(t => t.Id).ToList();
    }

    private static List<Track> DedupeById(List<Track> tracks)
    {
        var seen = new HashSet<Guid>();
        var output = new List<Track>(tracks.Count);
        foreach (var t in tracks)
        {
            if (seen.Add(t.Id)) output.Add(t);
        }
        return output;
    }

    private static List<Track> InterleaveForVariety(List<Track> input, int maxConsecutiveSameArtist, int maxPerArtist)
    {
        var groups = input
            .GroupBy(t => t.Album?.ArtistId ?? Guid.Empty)
            .Select(g => new Queue<Track>(g.Take(maxPerArtist)))
            .ToList();

        var output = new List<Track>(input.Count);
        Guid? lastArtistId = null;

        while (groups.Any(q => q.Count > 0))
        {
            var nonEmpty = groups.Where(q => q.Count > 0).ToList();
            var preferred = nonEmpty
                .Where(q => (q.Peek().Album?.ArtistId ?? Guid.Empty) != lastArtistId)
                .ToList();
            var pool = preferred.Count > 0 ? preferred : nonEmpty;

            var pick = pool.OrderByDescending(q => q.Count).First();
            var track = pick.Dequeue();
            output.Add(track);
            lastArtistId = track.Album?.ArtistId ?? Guid.Empty;
        }
        return output;
    }

    private static List<Guid> DriftBlend(List<Guid> existing, List<Guid> fresh, int driftPercent)
    {
        if (existing.Count == 0) return fresh;
        if (fresh.Count == 0) return existing;

        var evictCount = (int)Math.Ceiling(existing.Count * (driftPercent / 100.0));
        evictCount = Math.Max(1, Math.Min(evictCount, existing.Count - 1));

        var evict = existing.TakeLast(evictCount).ToHashSet();
        var keep = existing.Where(id => !evict.Contains(id)).ToList();

        var keepSet = keep.ToHashSet();
        var newcomers = fresh.Where(id => !keepSet.Contains(id)).Take(evictCount).ToList();

        var result = new List<Guid>(keep.Count + newcomers.Count);
        result.AddRange(keep);
        result.AddRange(newcomers);
        return result.Take(existing.Count).ToList();
    }

    private static List<GenreCluster> ClusterByGenre(List<ArtistPlayScore> topArtists, Dictionary<Guid, List<string>> genresByArtist)
    {
        var remaining = topArtists.OrderByDescending(a => a.Score).ToList();
        var clusters = new List<GenreCluster>();

        while (remaining.Count > 0)
        {
            var seed = remaining[0];
            var seedGenres = genresByArtist.TryGetValue(seed.ArtistId, out var sg) ? sg : new List<string>();

            var members = new List<ArtistPlayScore> { seed };
            for (int i = 1; i < remaining.Count; i++)
            {
                var candidate = remaining[i];
                var candidateGenres = genresByArtist.TryGetValue(candidate.ArtistId, out var cg) ? cg : new List<string>();
                if (seedGenres.Count == 0 && candidateGenres.Count == 0) continue;
                if (seedGenres.Intersect(candidateGenres, StringComparer.OrdinalIgnoreCase).Any())
                {
                    members.Add(candidate);
                }
            }

            var allGenres = members
                .SelectMany(m => genresByArtist.TryGetValue(m.ArtistId, out var g) ? g : new List<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .ToList();

            var dominantTag = allGenres
                .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key.Length)
                .Select(g => g.Key)
                .FirstOrDefault();

            clusters.Add(new GenreCluster
            {
                Members = members,
                DominantTag = dominantTag,
                AllGenres = allGenres.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                TotalScore = members.Sum(m => m.Score)
            });

            remaining.RemoveAll(r => members.Contains(r));
        }

        return clusters.Where(c => c.Members.Count >= 3 || c.Members.Count == 1 && c.TotalScore >= 5).ToList();
    }

    private static MusicAccessFilter BuildAccessFilter(UserProfile profile)
    {
        var musicRatings = profile.AllowedMusicRatings ?? new List<string>();
        return new MusicAccessFilter
        {
            HasAllLibraryAccess = profile.HasAllLibraryAccess,
            AllowedLibraryIds = profile.AllowedLibraryIds ?? new List<Guid>(),
            HasAllRatings = musicRatings.Count == 0,
            AllowedRatings = musicRatings,
            BlockUnratedContent = profile.BlockUnratedContent
        };
    }

    private static GeneratedMixSummaryVM MapSummary(GeneratedMix m) => new()
    {
        Id = m.Id,
        Slot = m.Slot,
        Name = m.Name,
        DescriptionTag = m.DescriptionTag,
        ArtworkUrl = m.ArtworkUrl,
        TrackCount = m.TrackOrder.Count,
        Kind = m.Kind.ToString(),
        GeneratedAt = m.GeneratedAt,
        LastDriftAt = m.LastDriftAt
    };

    private static ArtistTrackVM MapArtistTrack(Track t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Artist = t.Artist,
        TrackNumber = t.TrackNumber,
        DiscNumber = t.DiscNumber,
        DurationSeconds = t.DurationSeconds,
        ContentRating = t.ContentRating,
        AlbumId = t.AlbumId,
        AlbumTitle = t.Album?.Title,
        AlbumArtworkUrl = t.Album?.ArtworkUrl,
        IsLiked = false
    };

    private sealed class GenreCluster
    {
        public required List<ArtistPlayScore> Members { get; init; }
        public string? DominantTag { get; init; }
        public required List<string> AllGenres { get; init; }
        public required double TotalScore { get; init; }
    }

    public async Task<RadioQueueVM> StartRadioAsync(Guid profileId, MusicAccessFilter access, RadioSeed seed, int size)
    {
        return await BuildRadioQueueAsync(profileId, access, seed, Array.Empty<Guid>(), size);
    }

    public async Task<RadioQueueVM> ExtendRadioAsync(Guid profileId, MusicAccessFilter access, RadioSeed seed, IEnumerable<Guid> excludeTrackIds, int size)
    {
        return await BuildRadioQueueAsync(profileId, access, seed, excludeTrackIds, size);
    }

    private async Task<RadioQueueVM> BuildRadioQueueAsync(Guid profileId, MusicAccessFilter access, RadioSeed seed, IEnumerable<Guid> excludeTrackIds, int size)
    {
        var excludeSet = excludeTrackIds?.ToHashSet() ?? new HashSet<Guid>();
        var seedLabel = "Radio";
        var seedArtistId = seed.ArtistId;
        var seedGenres = new List<string>();
        var candidates = new List<Track>();

        if (seed.Kind == StationSeedKind.Artist && seed.ArtistId.HasValue)
        {
            var artistId = seed.ArtistId.Value;
            seedGenres = await _repo.GetAlbumGenresForArtistAsync(artistId);
            var artist = await _musicRepo.GetArtistByIdAsync(artistId, access);
            seedLabel = artist?.Name != null ? $"{artist.Name} Radio" : "Artist Radio";

            var seedShare = Math.Max(5, size * 30 / 100);
            var seedTracks = await _repo.GetTopTracksByArtistAsync(artistId, access, profileId, seedShare * 2, 3);
            candidates.AddRange(seedTracks.Where(t => !excludeSet.Contains(t.Id)).Take(seedShare));
        }
        else if (seed.Kind == StationSeedKind.Track && seed.TrackId.HasValue)
        {
            var track = await _musicRepo.GetTrackByIdAsync(seed.TrackId.Value, access);
            if (track == null) return new RadioQueueVM { SeedKind = seed.Kind.ToString(), SeedTrackId = seed.TrackId, SeedLabel = "Radio" };

            seedLabel = $"{track.Title} Radio";
            seedArtistId = null;
            if (track.AlbumId.HasValue)
            {
                var album = await _musicRepo.GetAlbumByIdAsync(track.AlbumId.Value, MusicAccessFilter.Unrestricted);
                if (album != null)
                {
                    seedArtistId = album.ArtistId;
                    if (!string.IsNullOrWhiteSpace(album.Genre)) seedGenres.Add(album.Genre);
                    if (seedGenres.Count == 0 && seedArtistId.HasValue)
                    {
                        seedGenres = await _repo.GetAlbumGenresForArtistAsync(seedArtistId.Value);
                    }
                }
            }

            if (!excludeSet.Contains(track.Id)) candidates.Add(track);
            if (seedArtistId.HasValue)
            {
                var seedShare = Math.Max(3, size * 20 / 100);
                var artistTracks = await _repo.GetTopTracksByArtistAsync(seedArtistId.Value, access, profileId, seedShare * 2, 3);
                candidates.AddRange(artistTracks.Where(t => !excludeSet.Contains(t.Id) && t.Id != track.Id).Take(seedShare));
            }
        }
        else if (seed.Kind == StationSeedKind.Genre && !string.IsNullOrWhiteSpace(seed.Genre))
        {
            seedGenres.Add(seed.Genre);
            seedLabel = $"{seed.Genre} Radio";
            var genreShare = Math.Max(15, size * 60 / 100);
            var genreTracks = await _repo.GetTracksByGenreAsync(seed.Genre, access, excludeSet, genreShare);
            candidates.AddRange(genreTracks);
        }

        if (seedGenres.Count > 0)
        {
            var allExcludes = excludeSet.Concat(candidates.Select(c => c.Id)).ToHashSet();
            var fillShare = Math.Max(5, size - candidates.Count);
            var sameGenre = await _repo.GetTopTracksByGenresAsync(seedGenres, access, seedArtistId, allExcludes, fillShare * 2);
            candidates.AddRange(sameGenre.Where(t => !allExcludes.Contains(t.Id)).Take(fillShare));
        }

        if (seedGenres.Count > 0 && candidates.Count < size)
        {
            var existingIds = candidates.Select(c => c.Id).Concat(excludeSet).ToHashSet();
            var liked = await _repo.GetLikedTracksByGenreAsync(profileId, seedGenres, access, (size - candidates.Count) * 2);
            foreach (var t in liked)
            {
                if (existingIds.Add(t.Id))
                {
                    candidates.Add(t);
                    if (candidates.Count >= size) break;
                }
            }
        }

        var deduped = DedupeById(candidates);
        var interleaved = InterleaveForVariety(deduped, maxConsecutiveSameArtist: 1, maxPerArtist: 3);
        var final = interleaved.Take(size).ToList();

        return new RadioQueueVM
        {
            SeedKind = seed.Kind.ToString(),
            SeedArtistId = seed.ArtistId,
            SeedTrackId = seed.TrackId,
            SeedGenre = seed.Genre,
            SeedLabel = seedLabel,
            Tracks = final.Select(MapMixTrack).ToList()
        };
    }

    public async Task<List<StationVM>> GetStationsForProfileAsync(Guid profileId)
    {
        var stations = await _repo.GetStationsForProfileAsync(profileId);
        var results = new List<StationVM>(stations.Count);
        foreach (var s in stations)
        {
            results.Add(await EnrichStationVMAsync(s));
        }
        return results;
    }

    public async Task<StationVM?> SaveStationAsync(Guid profileId, MusicAccessFilter access, string name, RadioSeed seed)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (trimmed == null) trimmed = await ResolveDefaultStationNameAsync(access, seed);
        if (string.IsNullOrWhiteSpace(trimmed)) return null;

        var station = new Station
        {
            ProfileId = profileId,
            Name = trimmed,
            SeedKind = seed.Kind,
            SeedArtistId = seed.ArtistId,
            SeedTrackId = seed.TrackId,
            SeedGenre = seed.Genre,
            CreatedAt = DateTime.UtcNow,
            LastPlayedAt = DateTime.UtcNow
        };
        await _repo.AddStationAsync(station);
        return await EnrichStationVMAsync(station);
    }

    public async Task DeleteStationAsync(Guid profileId, Guid stationId)
    {
        var station = await _repo.GetStationByIdAsync(stationId, profileId);
        if (station == null) return;
        await _repo.DeleteStationAsync(station);
    }

    public async Task TouchStationLastPlayedAsync(Guid profileId, Guid stationId)
    {
        var station = await _repo.GetStationByIdAsync(stationId, profileId);
        if (station == null) return;
        station.LastPlayedAt = DateTime.UtcNow;
        await _repo.UpdateStationAsync(station);
    }

    private async Task<StationVM> EnrichStationVMAsync(Station s)
    {
        string? artwork = null;
        string? subtitle = null;
        switch (s.SeedKind)
        {
            case StationSeedKind.Artist when s.SeedArtistId.HasValue:
                var artist = await _musicRepo.GetArtistByIdAsync(s.SeedArtistId.Value, MusicAccessFilter.Unrestricted);
                if (artist != null)
                {
                    artwork = artist.ArtworkUrl ?? artist.BackgroundUrl;
                    subtitle = $"Artist · {artist.Name}";
                }
                break;
            case StationSeedKind.Track when s.SeedTrackId.HasValue:
                var track = await _musicRepo.GetTrackByIdAsync(s.SeedTrackId.Value, MusicAccessFilter.Unrestricted);
                if (track != null)
                {
                    if (track.AlbumId.HasValue)
                    {
                        var album = await _musicRepo.GetAlbumByIdAsync(track.AlbumId.Value, MusicAccessFilter.Unrestricted);
                        artwork = album?.ArtworkUrl;
                    }
                    subtitle = $"Track · {track.Title}";
                }
                break;
            case StationSeedKind.Genre when !string.IsNullOrWhiteSpace(s.SeedGenre):
                subtitle = $"Genre · {s.SeedGenre}";
                break;
        }
        return new StationVM
        {
            Id = s.Id,
            Name = s.Name,
            SeedKind = s.SeedKind.ToString(),
            SeedArtistId = s.SeedArtistId,
            SeedTrackId = s.SeedTrackId,
            SeedGenre = s.SeedGenre,
            ArtworkUrl = artwork,
            SubtitleHint = subtitle,
            CreatedAt = s.CreatedAt,
            LastPlayedAt = s.LastPlayedAt
        };
    }

    private async Task<string?> ResolveDefaultStationNameAsync(MusicAccessFilter access, RadioSeed seed)
    {
        switch (seed.Kind)
        {
            case StationSeedKind.Artist when seed.ArtistId.HasValue:
                var artist = await _musicRepo.GetArtistByIdAsync(seed.ArtistId.Value, access);
                return artist != null ? $"{artist.Name} Radio" : null;
            case StationSeedKind.Track when seed.TrackId.HasValue:
                var track = await _musicRepo.GetTrackByIdAsync(seed.TrackId.Value, access);
                return track != null ? $"{track.Title} Radio" : null;
            case StationSeedKind.Genre when !string.IsNullOrWhiteSpace(seed.Genre):
                return $"{seed.Genre} Radio";
        }
        return null;
    }

    private static TrackVM MapMixTrack(Track t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        SortTitle = t.SortTitle,
        Artist = t.Artist,
        TrackNumber = t.TrackNumber,
        DiscNumber = t.DiscNumber,
        DurationSeconds = t.DurationSeconds,
        ContentRating = t.ContentRating,
        AlbumId = t.AlbumId,
        IsLiked = false,
        LockedFields = t.LockedFields ?? new List<string>()
    };

    public async Task<List<int>> GetYearsWithHistoryAsync(Guid profileId)
    {
        return await _repo.GetYearsWithHistoryAsync(profileId);
    }

    public async Task<YearRecapVM> GetYearRecapAsync(Guid profileId, MusicAccessFilter access, int year)
    {
        var plays = await _repo.GetPlaysForYearAsync(profileId, access, year);
        var recap = new YearRecapVM { Year = year, TotalPlays = plays.Count };
        if (plays.Count == 0)
        {
            recap.PlaysByDayOfWeek = Enumerable.Repeat(0, 7).ToList();
            recap.PlaysByHour = Enumerable.Repeat(0, 24).ToList();
            return recap;
        }

        recap.TotalListeningSeconds = plays.Sum(p => (long)p.DurationListenedSeconds);
        recap.DistinctTrackCount = plays.Select(p => p.TrackId).Distinct().Count();
        recap.DistinctArtistCount = plays.Where(p => p.ArtistId.HasValue).Select(p => p.ArtistId!.Value).Distinct().Count();
        recap.DistinctAlbumCount = plays.Where(p => p.AlbumId.HasValue).Select(p => p.AlbumId!.Value).Distinct().Count();

        recap.TopTracks = plays
            .GroupBy(p => p.TrackId)
            .Select(g => new YearRecapTrackVM
            {
                Id = g.Key,
                Title = g.First().TrackTitle,
                Artist = g.First().TrackArtist ?? g.First().ArtistName,
                AlbumTitle = g.First().AlbumTitle,
                AlbumArtworkUrl = g.First().AlbumArtworkUrl,
                PlayCount = g.Count()
            })
            .OrderByDescending(t => t.PlayCount)
            .Take(20)
            .ToList();

        recap.TopArtists = plays
            .Where(p => p.ArtistId.HasValue)
            .GroupBy(p => p.ArtistId!.Value)
            .Select(g => new YearRecapArtistVM
            {
                Id = g.Key,
                Name = g.First().ArtistName ?? "Unknown",
                ArtworkUrl = g.First().ArtistArtworkUrl,
                PlayCount = g.Count()
            })
            .OrderByDescending(a => a.PlayCount)
            .Take(10)
            .ToList();

        var genreGroups = plays
            .Where(p => !string.IsNullOrWhiteSpace(p.AlbumGenre))
            .GroupBy(p => p.AlbumGenre!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(10)
            .ToList();
        var genreTotal = genreGroups.Sum(g => g.Count);
        recap.TopGenres = genreGroups
            .Select(g => new YearRecapGenreVM
            {
                Name = g.Name,
                PlayCount = g.Count,
                Percent = genreTotal > 0 ? (int)Math.Round(g.Count * 100.0 / genreTotal) : 0
            })
            .ToList();

        var dowCounts = new int[7];
        var hourCounts = new int[24];
        foreach (var p in plays)
        {
            dowCounts[(int)p.PlayedAt.DayOfWeek]++;
            hourCounts[p.PlayedAt.Hour]++;
        }
        recap.PlaysByDayOfWeek = dowCounts.ToList();
        recap.PlaysByHour = hourCounts.ToList();

        var peakDow = 0;
        for (int i = 1; i < 7; i++) if (dowCounts[i] > dowCounts[peakDow]) peakDow = i;
        recap.PeakDayOfWeekLabel = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" }[peakDow];

        var peakHour = 0;
        for (int i = 1; i < 24; i++) if (hourCounts[i] > hourCounts[peakHour]) peakHour = i;
        recap.PeakHourLabel = FormatHour(peakHour);

        var newArtistIds = await _repo.GetArtistsFirstPlayedInYearAsync(profileId, access, year);
        recap.NewDiscoveries = plays
            .Where(p => p.ArtistId.HasValue && newArtistIds.Contains(p.ArtistId.Value))
            .GroupBy(p => p.ArtistId!.Value)
            .Select(g => new YearRecapArtistVM
            {
                Id = g.Key,
                Name = g.First().ArtistName ?? "Unknown",
                ArtworkUrl = g.First().ArtistArtworkUrl,
                PlayCount = g.Count()
            })
            .OrderByDescending(a => a.PlayCount)
            .Take(12)
            .ToList();

        return recap;
    }

    private static string FormatHour(int hour)
    {
        if (hour == 0) return "12 AM";
        if (hour == 12) return "12 PM";
        return hour < 12 ? $"{hour} AM" : $"{hour - 12} PM";
    }

    private static ArtistVM MapArtistVm(Domain.Entities.Media.Artist a) => new()
    {
        Id = a.Id,
        Name = a.Name,
        SortName = a.SortName,
        Biography = a.Biography,
        ArtworkUrl = a.ArtworkUrl,
        BackgroundUrl = a.BackgroundUrl,
        BannerUrl = a.BannerUrl,
        ClearLogoUrl = a.ClearLogoUrl,
        LibraryId = a.LibraryId,
        LockedFields = a.LockedFields ?? new List<string>()
    };

    private static readonly TimeSpan SimilarityCacheTtl = TimeSpan.FromDays(30);

    public async Task<List<ArtistVM>> GetSimilarArtistsAsync(Guid artistId, MusicAccessFilter access, CancellationToken cancellationToken)
    {
        var artist = await _musicRepo.GetArtistByIdAsync(artistId, access);
        if (artist == null) return new List<ArtistVM>();

        var cached = await _repo.GetSimilaritiesAsync(artistId);
        var fresh = cached.Any() && (DateTime.UtcNow - cached.Max(s => s.FetchedAt)) < SimilarityCacheTtl;

        if (!fresh)
        {
            var provider = _listeningProviders.FirstOrDefault(p => p.Id == "lastfm_listening");
            if (provider != null)
            {
                try
                {
                    var fetched = await provider.GetSimilarArtistsAsync(artist.Name, 30, cancellationToken);
                    if (fetched.Count > 0)
                    {
                        var entries = fetched.Select(f => new ArtistSimilarity
                        {
                            ArtistId = artistId,
                            SimilarArtistName = f.Name,
                            Score = f.Score,
                            Source = "lastfm",
                            FetchedAt = DateTime.UtcNow
                        }).ToList();
                        await _repo.ReplaceSimilaritiesAsync(artistId, entries);
                        cached = entries;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Last.fm similar lookup failed for {Artist}", artist.Name);
                }
            }
        }

        if (cached.Count == 0) return new List<ArtistVM>();

        var names = cached.OrderByDescending(c => c.Score).Select(c => c.SimilarArtistName).ToList();
        var inLibrary = await _repo.GetArtistsByNamesAsync(names, access);

        var ordered = new List<ArtistVM>();
        foreach (var name in names)
        {
            if (inLibrary.TryGetValue(name, out var artistEntity))
            {
                ordered.Add(MapArtistVm(artistEntity));
                if (ordered.Count >= 12) break;
            }
        }
        return ordered;
    }

    public async Task<List<string>> GetArtistTagsAsync(Guid artistId, CancellationToken cancellationToken)
    {
        var artist = await _musicRepo.GetArtistByIdAsync(artistId, MusicAccessFilter.Unrestricted);
        if (artist == null) return new List<string>();

        var cached = await _repo.GetArtistTagsAsync(artistId);
        var fresh = cached.Any() && (DateTime.UtcNow - cached.Max(t => t.FetchedAt)) < SimilarityCacheTtl;

        if (!fresh)
        {
            var provider = _listeningProviders.FirstOrDefault(p => p.Id == "lastfm_listening");
            if (provider != null)
            {
                try
                {
                    var fetched = await provider.GetArtistTopTagsAsync(artist.Name, 20, cancellationToken);
                    if (fetched.Count > 0)
                    {
                        var entries = fetched.Select(f => new ArtistTag
                        {
                            ArtistId = artistId,
                            Tag = f.Tag,
                            Weight = f.Weight,
                            Source = "lastfm",
                            FetchedAt = DateTime.UtcNow
                        }).ToList();
                        await _repo.ReplaceArtistTagsAsync(artistId, entries);
                        cached = entries;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Last.fm tags lookup failed for {Artist}", artist.Name);
                }
            }
        }

        return cached.OrderByDescending(t => t.Weight).Select(t => t.Tag).ToList();
    }

    private static readonly Dictionary<string, (string[] Tags, string[] GenreKeywords)> MoodDefinitions = new()
    {
        ["Focus"] = (
            new[] { "instrumental", "ambient", "classical", "post-rock", "soundtrack", "minimal", "lo-fi", "study" },
            new[] { "classical", "ambient", "instrumental", "soundtrack", "score", "jazz" }
        ),
        ["Energetic"] = (
            new[] { "electronic", "dance", "edm", "house", "techno", "rock", "punk", "hip-hop", "high-energy", "workout" },
            new[] { "electronic", "dance", "rock", "metal", "punk", "hip-hop", "rap", "edm" }
        ),
        ["Chill"] = (
            new[] { "chill", "chillout", "downtempo", "lounge", "acoustic", "mellow", "indie", "folk", "trip-hop" },
            new[] { "indie", "folk", "acoustic", "alternative", "jazz", "soul" }
        ),
        ["Late Night"] = (
            new[] { "ambient", "jazz", "blues", "soul", "r&b", "trip-hop", "downtempo", "vaporwave", "nocturnal" },
            new[] { "jazz", "blues", "r&b", "soul", "ambient", "trip-hop" }
        )
    };

    public async Task RefreshWeeklyMixesForProfileAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await _userRepo.GetProfileByIdAsync(profileId);
        if (profile == null) return;
        var settings = await _settingsRepo.GetSettingsAsync();
        if (!settings.EnableWeeklyMixes) return;
        var access = BuildAccessFilter(profile);

        try
        {
            await GenerateDiscoverMixAsync(profileId, access, settings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discover Mix generation failed for {Profile}", profileId);
        }

        try
        {
            await GenerateMoodMixesAsync(profileId, access, settings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mood Mix generation failed for {Profile}", profileId);
        }

        try
        {
            await GenerateReleaseRadarAsync(profileId, access, settings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Release Radar generation failed for {Profile}", profileId);
        }

        await _notifier.NotifyMusicMixesUpdatedAsync(profileId);
    }

    public async Task RefreshWeeklyMixesForAllAsync(CancellationToken cancellationToken)
    {
        var profileIds = await _repo.GetProfileIdsWithRecentActivityAsync(30);
        foreach (var pid in profileIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await RefreshWeeklyMixesForProfileAsync(pid, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Weekly mix refresh failed for profile {ProfileId}", pid); }
        }

        var settings = await _settingsRepo.GetSettingsForUpdateAsync();
        settings.WeeklyMixLastRefreshedAt = DateTime.UtcNow;
        await _settingsRepo.SaveChangesAsync();
    }

    private async Task GenerateDiscoverMixAsync(Guid profileId, MusicAccessFilter access, Domain.Entities.Settings.ServerSetting settings, CancellationToken cancellationToken)
    {
        var topArtists = await _repo.GetTopArtistsForProfileAsync(profileId, access, 90, 30);
        if (topArtists.Count < 3) return;

        var playedTrackIds = await GetRecentlyPlayedTrackIdsAsync(profileId, access);
        var candidates = new List<Track>();
        var seenTrackIds = new HashSet<Guid>(playedTrackIds);
        var seenArtistIds = topArtists.Select(a => a.ArtistId).ToHashSet();

        var provider = _listeningProviders.FirstOrDefault(p => p.Id == "lastfm_listening");
        if (provider != null)
        {
            foreach (var seed in topArtists.Take(15))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var similar = await provider.GetSimilarArtistsAsync(seed.ArtistName, 10, cancellationToken);
                    var inLib = await _repo.GetArtistsByNamesAsync(similar.Select(s => s.Name), access);
                    foreach (var sa in similar)
                    {
                        if (!inLib.TryGetValue(sa.Name, out var artistEntity)) continue;
                        if (!seenArtistIds.Add(artistEntity.Id)) continue;
                        var tracks = await _repo.GetTopTracksByArtistAsync(artistEntity.Id, access, profileId, 3, 2);
                        foreach (var t in tracks)
                        {
                            if (seenTrackIds.Add(t.Id)) candidates.Add(t);
                        }
                        if (candidates.Count >= settings.DailyMixSize * 2) break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Discover Mix Last.fm lookup failed for {Artist}", seed.ArtistName);
                }
                if (candidates.Count >= settings.DailyMixSize * 2) break;
            }
        }

        if (candidates.Count < settings.DailyMixSize)
        {
            var genres = (await _repo.GetGenresForArtistsAsync(topArtists.Select(a => a.ArtistId)))
                .SelectMany(kv => kv.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var genreFill = await _repo.GetTopTracksByGenresAsync(genres, access, null, seenTrackIds, settings.DailyMixSize);
            foreach (var t in genreFill)
            {
                if (seenTrackIds.Add(t.Id)) candidates.Add(t);
            }
        }

        if (candidates.Count == 0) return;

        var deduped = DedupeById(candidates);
        var interleaved = InterleaveForVariety(deduped, 1, 3).Take(settings.DailyMixSize).ToList();
        var artwork = interleaved.FirstOrDefault(t => t.Album != null && !string.IsNullOrWhiteSpace(t.Album.ArtworkUrl))?.Album?.ArtworkUrl;

        var existing = await _repo.GetMixesForProfileAsync(profileId, GeneratedMixKind.DiscoverMix);
        var slot1 = existing.FirstOrDefault(m => m.Slot == 1);
        var trackIds = interleaved.Select(t => t.Id).ToList();
        if (slot1 != null)
        {
            slot1.Name = "Discover Mix";
            slot1.DescriptionTag = "Fresh finds";
            slot1.ArtworkUrl = artwork;
            slot1.TrackOrder = trackIds;
            await _repo.SaveMixAsync(slot1);
        }
        else
        {
            await _repo.SaveMixAsync(new GeneratedMix
            {
                ProfileId = profileId,
                Slot = 1,
                Name = "Discover Mix",
                DescriptionTag = "Fresh finds",
                Kind = GeneratedMixKind.DiscoverMix,
                ArtworkUrl = artwork,
                GeneratedAt = DateTime.UtcNow,
                TrackOrder = trackIds
            });
        }
    }

    private async Task GenerateMoodMixesAsync(Guid profileId, MusicAccessFilter access, Domain.Entities.Settings.ServerSetting settings, CancellationToken cancellationToken)
    {
        var topArtists = await _repo.GetTopArtistsForProfileAsync(profileId, access, 180, 50);
        if (topArtists.Count < 3) return;

        var provider = _listeningProviders.FirstOrDefault(p => p.Id == "lastfm_listening");
        var artistTags = new Dictionary<Guid, List<string>>();
        if (provider != null)
        {
            foreach (var artist in topArtists.Take(30))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var cached = await _repo.GetArtistTagsAsync(artist.ArtistId);
                    if (cached.Count == 0 || (DateTime.UtcNow - cached.Max(t => t.FetchedAt)) > SimilarityCacheTtl)
                    {
                        var fetched = await provider.GetArtistTopTagsAsync(artist.ArtistName, 10, cancellationToken);
                        if (fetched.Count > 0)
                        {
                            cached = fetched.Select(f => new ArtistTag { ArtistId = artist.ArtistId, Tag = f.Tag, Weight = f.Weight, Source = "lastfm", FetchedAt = DateTime.UtcNow }).ToList();
                            await _repo.ReplaceArtistTagsAsync(artist.ArtistId, cached);
                        }
                    }
                    artistTags[artist.ArtistId] = cached.Select(t => t.Tag.ToLowerInvariant()).ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Mood mix tag lookup failed for {Artist}", artist.ArtistName);
                }
            }
        }

        var artistGenres = await _repo.GetGenresForArtistsAsync(topArtists.Select(a => a.ArtistId));

        var moodList = MoodDefinitions.ToList();
        for (int i = 0; i < moodList.Count; i++)
        {
            var (moodName, def) = (moodList[i].Key, moodList[i].Value);
            var matchingArtists = new List<Guid>();
            foreach (var artist in topArtists)
            {
                var tags = artistTags.TryGetValue(artist.ArtistId, out var t) ? t : new List<string>();
                var genres = artistGenres.TryGetValue(artist.ArtistId, out var g) ? g.Select(s => s.ToLowerInvariant()).ToList() : new List<string>();
                var tagMatch = tags.Any(tag => def.Tags.Any(mt => tag.Contains(mt, StringComparison.OrdinalIgnoreCase)));
                var genreMatch = genres.Any(genre => def.GenreKeywords.Any(gk => genre.Contains(gk, StringComparison.OrdinalIgnoreCase)));
                if (tagMatch || genreMatch) matchingArtists.Add(artist.ArtistId);
            }

            if (matchingArtists.Count == 0) continue;

            var candidates = new List<Track>();
            var seen = new HashSet<Guid>();
            foreach (var artistId in matchingArtists.Take(20))
            {
                var tracks = await _repo.GetTopTracksByArtistAsync(artistId, access, profileId, 4, 2);
                foreach (var t in tracks)
                {
                    if (seen.Add(t.Id)) candidates.Add(t);
                }
                if (candidates.Count >= settings.DailyMixSize) break;
            }

            if (candidates.Count == 0) continue;

            var interleaved = InterleaveForVariety(DedupeById(candidates), 1, 3).Take(settings.DailyMixSize).ToList();
            var artwork = interleaved.FirstOrDefault(t => t.Album != null && !string.IsNullOrWhiteSpace(t.Album.ArtworkUrl))?.Album?.ArtworkUrl;
            var trackIds = interleaved.Select(t => t.Id).ToList();

            var existing = await _repo.GetMixesForProfileAsync(profileId, GeneratedMixKind.MoodMix);
            var slot = i + 1;
            var slotMix = existing.FirstOrDefault(m => m.Slot == slot);
            if (slotMix != null)
            {
                slotMix.Name = moodName;
                slotMix.DescriptionTag = "Mood";
                slotMix.ArtworkUrl = artwork;
                slotMix.TrackOrder = trackIds;
                await _repo.SaveMixAsync(slotMix);
            }
            else
            {
                await _repo.SaveMixAsync(new GeneratedMix
                {
                    ProfileId = profileId,
                    Slot = slot,
                    Name = moodName,
                    DescriptionTag = "Mood",
                    Kind = GeneratedMixKind.MoodMix,
                    ArtworkUrl = artwork,
                    GeneratedAt = DateTime.UtcNow,
                    TrackOrder = trackIds
                });
            }
        }
    }

    private async Task<HashSet<Guid>> GetRecentlyPlayedTrackIdsAsync(Guid profileId, MusicAccessFilter access)
    {
        var recent = await _repo.GetRecentTopPlayedTracksAsync(profileId, access, 365, 500);
        return recent.Select(t => t.Id).ToHashSet();
    }

    private async Task GenerateReleaseRadarAsync(Guid profileId, MusicAccessFilter access, Domain.Entities.Settings.ServerSetting settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var activeArtistIds = await _repo.GetActiveArtistIdsForProfileAsync(profileId, 60);
        if (activeArtistIds.Count < 3)
        {
            await _repo.DeleteMixesForProfileAsync(profileId, GeneratedMixKind.ReleaseRadar);
            return;
        }

        var candidates = await _repo.GetRecentlyAddedTracksByArtistsAsync(activeArtistIds, access, 30, 100);
        if (candidates.Count == 0)
        {
            await _repo.DeleteMixesForProfileAsync(profileId, GeneratedMixKind.ReleaseRadar);
            return;
        }

        var deduped = DedupeById(candidates);
        var interleaved = InterleaveForVariety(deduped, 1, 3).Take(Math.Max(20, settings.DailyMixSize)).ToList();
        var trackIds = interleaved.Select(t => t.Id).ToList();
        var artwork = interleaved
            .Select(t => t.Album?.ArtworkUrl)
            .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

        var existing = await _repo.GetMixesForProfileAsync(profileId, GeneratedMixKind.ReleaseRadar);
        var slotMix = existing.FirstOrDefault(m => m.Slot == 1);
        if (slotMix != null)
        {
            slotMix.Name = "Release Radar";
            slotMix.DescriptionTag = "New from artists you love";
            slotMix.ArtworkUrl = artwork;
            slotMix.TrackOrder = trackIds;
            await _repo.SaveMixAsync(slotMix);
        }
        else
        {
            await _repo.SaveMixAsync(new GeneratedMix
            {
                ProfileId = profileId,
                Slot = 1,
                Name = "Release Radar",
                DescriptionTag = "New from artists you love",
                Kind = GeneratedMixKind.ReleaseRadar,
                ArtworkUrl = artwork,
                GeneratedAt = DateTime.UtcNow,
                TrackOrder = trackIds
            });
        }
    }
}

public class GeneratedMixSummaryVM
{
    public Guid Id { get; set; }
    public int Slot { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DescriptionTag { get; set; }
    public string? ArtworkUrl { get; set; }
    public int TrackCount { get; set; }
    public string Kind { get; set; } = "DailyMix";
    public DateTime GeneratedAt { get; set; }
    public DateTime? LastDriftAt { get; set; }
}

public class GeneratedMixDetailVM
{
    public Guid Id { get; set; }
    public int Slot { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DescriptionTag { get; set; }
    public string? ArtworkUrl { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime? LastDriftAt { get; set; }
    public List<TrackVM> Tracks { get; set; } = new();
}

public class BecauseYouPlayedRowVM
{
    public string Heading { get; set; } = string.Empty;
    public Guid SeedArtistId { get; set; }
    public List<ArtistTrackVM> Tracks { get; set; } = new();
}
