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

    public async Task<List<SmartPlaylistSummaryVM>> ListAsync(Guid profileId, MusicAccessFilter access)
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
                UpdatedAt = r.UpdatedAt
            });
        }
        return summaries;
    }

    public async Task<SmartPlaylistDetailVM?> GetAsync(Guid id, Guid profileId, MusicAccessFilter access)
    {
        var row = await _repo.GetByIdAsync(id, profileId);
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
            UpdatedAt = row.UpdatedAt
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
        existing.Description = request.Description;
        existing.ArtworkUrl = request.ArtworkUrl;
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

    public Task<int> PreviewCountAsync(Guid profileId, MusicAccessFilter access, PlaylistMediaType mediaType, SmartPlaylistDefinition definition) =>
        _evaluator.CountAsync(definition, mediaType, profileId, access);

    public async Task<SmartPlaylistItemsVM> GetItemsAsync(Guid id, Guid profileId, MusicAccessFilter access)
    {
        var row = await _repo.GetByIdAsync(id, profileId);
        if (row == null) return new SmartPlaylistItemsVM { MediaType = PlaylistMediaType.Music };
        var def = ParseDefinition(row);
        var items = await _evaluator.EvaluateAsync(def, row.MediaType, profileId, access);

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
