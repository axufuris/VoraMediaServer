using System.Text.Json;
using System.Text.Json.Serialization;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Media.SmartPlaylists;

public sealed class SmartPlaylistManager : ISmartPlaylistManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISmartPlaylistRepository _repo;
    private readonly ISmartPlaylistEvaluator _evaluator;
    private readonly IMusicRepository _musicRepo;

    public SmartPlaylistManager(ISmartPlaylistRepository repo, ISmartPlaylistEvaluator evaluator, IMusicRepository musicRepo)
    {
        _repo = repo;
        _evaluator = evaluator;
        _musicRepo = musicRepo;
    }

    public async Task<List<SmartPlaylistSummaryVM>> ListAsync(Guid profileId, PlaylistAccessFilter access)
    {
        var rows = await _repo.GetForProfileAsync(profileId);
        var summaries = new List<SmartPlaylistSummaryVM>(rows.Count);
        foreach (var r in rows)
        {
            var def = ParseDefinition(r);
            int count = 0;
            try { count = await _evaluator.CountAsync(def, r.MediaType, profileId, access); }
            catch { count = 0; }
            summaries.Add(new SmartPlaylistSummaryVM
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                ArtworkUrl = r.ArtworkUrl,
                MediaType = r.MediaType,
                TrackCount = count,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                IsShared = r.IsShared,
                IsOwner = true
            });
        }
        return summaries;
    }

    // Readable by the owner or, once shared, by anyone. The rules themselves are
    // part of what is shared, so a viewer sees the definition — but only the
    // owner can change it, because updates still load through GetByIdAsync.
    public async Task<SmartPlaylistDetailVM?> GetAsync(Guid id, Guid profileId, PlaylistAccessFilter access)
    {
        var row = await _repo.GetVisibleAsync(id, profileId);
        if (row == null) return null;
        var def = ParseDefinition(row);
        return new SmartPlaylistDetailVM
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            ArtworkUrl = row.ArtworkUrl,
            MediaType = row.MediaType,
            Definition = def,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            IsShared = row.IsShared,
            IsOwner = row.ProfileId == profileId,
            OwnerName = row.Profile?.Name ?? string.Empty
        };
    }

    public async Task<SmartPlaylistSummaryVM> CreateAsync(Guid profileId, SmartPlaylistSaveRequest request)
    {
        var entity = new SmartPlaylist
        {
            ProfileId = profileId,
            Name = request.Name?.Trim() ?? "Smart Playlist",
            Description = request.Description,
            ArtworkUrl = request.ArtworkUrl,
            MediaType = request.MediaType,
            RulesJson = SerializeRules(request.Definition.Root),
            Limit = request.Definition.Limit,
            SortBy = request.Definition.SortBy.ToString(),
            SortDirection = request.Definition.SortDirection.ToString()
        };
        await _repo.AddAsync(entity);
        return new SmartPlaylistSummaryVM
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            ArtworkUrl = entity.ArtworkUrl,
            MediaType = entity.MediaType,
            TrackCount = 0,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    public async Task<SmartPlaylistSummaryVM?> UpdateAsync(Guid id, Guid profileId, SmartPlaylistSaveRequest request)
    {
        var existing = await _repo.GetByIdAsync(id, profileId);
        if (existing == null) return null;
        existing.Name = request.Name?.Trim() ?? existing.Name;
        existing.Description = request.Description ?? existing.Description;
        existing.ArtworkUrl = request.ArtworkUrl ?? existing.ArtworkUrl;
        existing.MediaType = request.MediaType;
        existing.RulesJson = SerializeRules(request.Definition.Root);
        existing.Limit = request.Definition.Limit;
        existing.SortBy = request.Definition.SortBy.ToString();
        existing.SortDirection = request.Definition.SortDirection.ToString();
        await _repo.UpdateAsync(existing);
        return new SmartPlaylistSummaryVM
        {
            Id = existing.Id,
            Name = existing.Name,
            Description = existing.Description,
            ArtworkUrl = existing.ArtworkUrl,
            MediaType = existing.MediaType,
            TrackCount = 0,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = existing.UpdatedAt
        };
    }

    public Task DeleteAsync(Guid id, Guid profileId) => _repo.DeleteAsync(id, profileId);

    // Other people's shared smart playlists. Counted the same way they are shown
    // — the owner's taste, the viewer's visibility — and one that would show the
    // viewer nothing is left out rather than listed as empty, for the same
    // reason as manual playlists: its title alone can be what a parent's
    // controls are there to keep from a child.
    public async Task<List<SmartPlaylistSummaryVM>> GetSharedByOthersAsync(Guid viewerProfileId, PlaylistAccessFilter access)
    {
        var rows = await _repo.GetSharedByOthersAsync(viewerProfileId);
        var shared = new List<SmartPlaylistSummaryVM>(rows.Count);

        foreach (var r in rows)
        {
            int count;
            try { count = await _evaluator.CountAsync(ParseDefinition(r), r.MediaType, r.ProfileId, access); }
            catch { count = 0; }
            if (count == 0) continue;

            shared.Add(new SmartPlaylistSummaryVM
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                ArtworkUrl = r.ArtworkUrl,
                MediaType = r.MediaType,
                TrackCount = count,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                IsShared = true,
                IsOwner = false,
                OwnerName = r.Profile?.Name ?? string.Empty
            });
        }

        return shared;
    }

    public Task<bool> SetSharedAsync(Guid id, Guid ownerProfileId, bool isShared) =>
        _repo.SetSharedAsync(id, ownerProfileId, isShared);

    // Copies the RULES, not the results. The copy is the viewer's playlist, so
    // from then on it is evaluated entirely on the viewer's own plays, ratings
    // and parental controls — a copy of "Most Played" becomes the viewer's most
    // played. It starts unshared, so saving someone's playlist does not add a
    // duplicate to everyone's Shared tab.
    public async Task<Guid?> CopyAsync(Guid sourceId, Guid viewerProfileId)
    {
        var source = await _repo.GetVisibleAsync(sourceId, viewerProfileId);
        if (source == null) return null;

        var copy = new SmartPlaylist
        {
            ProfileId = viewerProfileId,
            Name = source.Name,
            Description = source.Description,
            ArtworkUrl = source.ArtworkUrl,
            MediaType = source.MediaType,
            RulesJson = source.RulesJson,
            Limit = source.Limit,
            SortBy = source.SortBy,
            SortDirection = source.SortDirection,
            IsShared = false
        };

        await _repo.AddAsync(copy);
        return copy.Id;
    }

    public Task<int> PreviewCountAsync(Guid profileId, PlaylistAccessFilter access, PlaylistMediaType mediaType, SmartPlaylistDefinition definition) =>
        _evaluator.CountAsync(definition, mediaType, profileId, access);

    // The profile a smart playlist is evaluated FOR does two jobs, and sharing
    // splits them. Rules such as "tracks I've played ten times" or "films I've
    // rated four stars" describe the OWNER's taste, so they read the owner's
    // plays and ratings — otherwise opening Andy's "Most Played" would show the
    // viewer their own most played under Andy's name. What may be SHOWN is the
    // viewer's business, so visibility comes from the viewer's parental
    // controls. The hearts on each row stay the viewer's own likes.
    public async Task<SmartPlaylistItemsVM> GetItemsAsync(Guid id, Guid profileId, PlaylistAccessFilter access)
    {
        var row = await _repo.GetVisibleAsync(id, profileId);
        if (row == null) return new SmartPlaylistItemsVM { MediaType = PlaylistMediaType.Music };
        var def = ParseDefinition(row);
        var items = await _evaluator.EvaluateAsync(def, row.MediaType, row.ProfileId, access);

        var vm = new SmartPlaylistItemsVM { MediaType = row.MediaType };

        switch (row.MediaType)
        {
            case PlaylistMediaType.Music:
            {
                var tracks = items.OfType<Track>().ToList();
                var liked = tracks.Count == 0 ? new HashSet<Guid>() : await _musicRepo.GetLikedTrackIdsAsync(profileId, tracks.Select(t => t.Id));
                vm.Tracks = tracks.Select(t => new ArtistTrackVM
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
                    IsLiked = liked.Contains(t.Id)
                }).ToList();
                break;
            }
            case PlaylistMediaType.Movies:
            {
                var movies = items.OfType<Movie>().ToList();
                vm.Movies = movies.Select(m => new SmartPlaylistMovieVM
                {
                    Id = m.Id,
                    Title = m.Title,
                    Year = m.ReleaseDate?.Year,
                    PosterUrl = m.PosterUrl,
                    BackgroundUrl = m.BackgroundUrl,
                    DurationSeconds = m.MediaParts.FirstOrDefault()?.Duration is TimeSpan ts ? (int)ts.TotalSeconds : (int?)null,
                    ContentRating = m.ContentRating,
                    IsWatched = false
                }).ToList();
                break;
            }
            case PlaylistMediaType.Shows:
            {
                var episodes = items.OfType<Episode>().ToList();
                vm.Episodes = episodes.Select(e => new SmartPlaylistEpisodeVM
                {
                    Id = e.Id,
                    Title = e.Title,
                    ShowTitle = e.Season?.TvShow?.Title,
                    SeasonNumber = e.Season?.SeasonNumber,
                    EpisodeNumber = e.EpisodeNumber,
                    PosterUrl = e.PosterUrl ?? e.Season?.TvShow?.PosterUrl,
                    DurationSeconds = e.MediaParts.FirstOrDefault()?.Duration is TimeSpan ts ? (int)ts.TotalSeconds : (int?)null,
                    ContentRating = e.ContentRating,
                    IsWatched = false
                }).ToList();
                break;
            }
        }

        return vm;
    }

    private static SmartPlaylistDefinition ParseDefinition(SmartPlaylist row)
    {
        var def = new SmartPlaylistDefinition();
        try
        {
            var group = JsonSerializer.Deserialize<SmartPlaylistRuleGroup>(row.RulesJson, JsonOptions);
            if (group != null) def.Root = group;
        }
        catch { /* ignore malformed rules — treat as empty group */ }

        def.Limit = row.Limit;
        if (Enum.TryParse<SmartPlaylistSortBy>(row.SortBy, true, out var sb)) def.SortBy = sb;
        if (Enum.TryParse<SmartPlaylistSortDirection>(row.SortDirection, true, out var sd)) def.SortDirection = sd;
        return def;
    }

    private static string SerializeRules(SmartPlaylistRuleGroup root) =>
        JsonSerializer.Serialize(root, JsonOptions);
}
