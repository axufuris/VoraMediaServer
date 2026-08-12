using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;

namespace Vora.Infrastructure.Persistence.Repositories;

public sealed class SmartPlaylistEvaluator : ISmartPlaylistEvaluator
{
    private readonly VoraDbContext _context;

    public SmartPlaylistEvaluator(VoraDbContext context)
    {
        _context = context;
    }

    public async Task<List<MediaItem>> EvaluateAsync(SmartPlaylistDefinition definition, PlaylistMediaType mediaType, Guid profileId, MusicAccessFilter access)
    {
        var ids = await BuildIdQuery(definition, mediaType, profileId, access).ToListAsync();
        if (ids.Count == 0) return new List<MediaItem>();

        var idSet = ids.ToHashSet();
        IQueryable<MediaItem> fetchQuery = mediaType switch
        {
            PlaylistMediaType.Music => _context.Tracks
                .AsNoTracking()
                .Include(t => t.Album)!
                    .ThenInclude(a => a!.Artist)
                .Where(t => idSet.Contains(t.Id))
                .Cast<MediaItem>(),
            PlaylistMediaType.Movies => _context.Movies
                .AsNoTracking()
                .Include(m => m.MediaParts)
                .Where(m => idSet.Contains(m.Id))
                .Cast<MediaItem>(),
            PlaylistMediaType.Shows => _context.Episodes
                .AsNoTracking()
                .Include(e => e.Season)
                    .ThenInclude(s => s.TvShow)
                .Include(e => e.MediaParts)
                .Where(e => idSet.Contains(e.Id))
                .Cast<MediaItem>(),
            _ => _context.MediaItems.AsNoTracking().Where(m => idSet.Contains(m.Id))
        };

        var fetched = await fetchQuery.ToListAsync();
        var byId = fetched.ToDictionary(m => m.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public Task<List<Guid>> EvaluateIdsAsync(SmartPlaylistDefinition definition, PlaylistMediaType mediaType, Guid profileId, MusicAccessFilter access) =>
        BuildIdQuery(definition, mediaType, profileId, access).ToListAsync();

    public Task<int> CountAsync(SmartPlaylistDefinition definition, PlaylistMediaType mediaType, Guid profileId, MusicAccessFilter access) =>
        mediaType switch
        {
            PlaylistMediaType.Music => BuildMusicRowQuery(definition, profileId, access).CountAsync(),
            PlaylistMediaType.Movies => BuildMovieRowQuery(definition, profileId, access).CountAsync(),
            PlaylistMediaType.Shows => BuildEpisodeRowQuery(definition, profileId, access).CountAsync(),
            _ => Task.FromResult(0)
        };

    private IQueryable<Guid> BuildIdQuery(SmartPlaylistDefinition definition, PlaylistMediaType mediaType, Guid profileId, MusicAccessFilter access)
    {
        int limit = definition.Limit.HasValue && definition.Limit.Value > 0 ? definition.Limit.Value : 2000;
        switch (mediaType)
        {
            case PlaylistMediaType.Music:
            {
                var q = BuildMusicRowQuery(definition, profileId, access);
                var ordered = ApplyMusicSort(q, definition.SortBy, definition.SortDirection);
                return ordered.Take(limit).Select(r => r.TrackId);
            }
            case PlaylistMediaType.Movies:
            {
                var q = BuildMovieRowQuery(definition, profileId, access);
                var ordered = ApplyVideoSort(q, definition.SortBy, definition.SortDirection);
                return ordered.Take(limit).Select(r => r.ItemId);
            }
            case PlaylistMediaType.Shows:
            {
                var q = BuildEpisodeRowQuery(definition, profileId, access);
                var ordered = ApplyVideoSort(q, definition.SortBy, definition.SortDirection);
                return ordered.Take(limit).Select(r => r.ItemId);
            }
            default:
                return _context.MediaItems.AsNoTracking().Where(m => false).Select(m => m.Id);
        }
    }

    private IQueryable<MusicRow> BuildMusicRowQuery(SmartPlaylistDefinition definition, Guid profileId, MusicAccessFilter access)
    {
        var baseQuery = _context.Tracks
            .AsNoTracking()
            .Where(t => t.MissingSince == null)
            .Where(t => t.AlbumId != null && t.Album != null);

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            baseQuery = baseQuery.Where(t => t.Album != null && allowed.Contains(t.Album.LibraryId));
        }

        if (!access.HasAllRatings)
        {
            var allowedRatings = access.AllowedRatings;
            if (access.BlockUnratedContent)
            {
                baseQuery = baseQuery.Where(t => t.ContentRating != null && allowedRatings.Contains(t.ContentRating));
            }
            else
            {
                baseQuery = baseQuery.Where(t => t.ContentRating == null || allowedRatings.Contains(t.ContentRating));
            }
        }

        var playStats = _context.TrackPlayHistory
            .Where(p => p.ProfileId == profileId)
            .GroupBy(p => p.TrackId)
            .Select(g => new { TrackId = g.Key, PlayCount = g.Count(), LastPlayedAt = (DateTime?)g.Max(p => p.PlayedAt) });

        var likedTrackIds = _context.TrackLikes
            .Where(l => l.ProfileId == profileId)
            .Select(l => l.TrackId);

        var rowQuery = baseQuery.Select(t => new MusicRow
        {
            TrackId = t.Id,
            Title = t.Title,
            Artist = t.Artist,
            AlbumId = t.AlbumId,
            AlbumTitle = t.Album!.Title,
            AlbumArtist = t.Album.AlbumArtist,
            AlbumYear = t.Album.Year,
            AlbumGenre = t.Album.Genre,
            IsCompilation = t.Album.IsCompilation,
            ArtistId = t.Album.ArtistId,
            ArtistName = t.Album.Artist != null ? t.Album.Artist.Name : null,
            LibraryId = t.Album.LibraryId,
            ContentRating = t.ContentRating,
            TrackNumber = t.TrackNumber,
            DiscNumber = t.DiscNumber,
            DurationSeconds = t.DurationSeconds,
            AddedAt = t.AddedAt,
            PlayCount = playStats.Where(s => s.TrackId == t.Id).Select(s => s.PlayCount).FirstOrDefault(),
            LastPlayedAt = playStats.Where(s => s.TrackId == t.Id).Select(s => s.LastPlayedAt).FirstOrDefault(),
            Liked = likedTrackIds.Contains(t.Id)
        });

        var predicate = BuildGroupPredicate<MusicRow>(definition.Root, MusicFieldAccessor);
        if (predicate != null) rowQuery = rowQuery.Where(predicate);

        return rowQuery;
    }

    private IQueryable<VideoRow> BuildMovieRowQuery(SmartPlaylistDefinition definition, Guid profileId, MusicAccessFilter access)
    {
        var baseQuery = _context.Movies.AsNoTracking().Where(m => m.MissingSince == null);

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            baseQuery = baseQuery.Where(m => allowed.Contains(m.LibraryId));
        }

        if (!access.HasAllRatings)
        {
            var allowedRatings = access.AllowedRatings;
            if (access.BlockUnratedContent)
                baseQuery = baseQuery.Where(m => m.ContentRating != null && allowedRatings.Contains(m.ContentRating));
            else
                baseQuery = baseQuery.Where(m => m.ContentRating == null || allowedRatings.Contains(m.ContentRating));
        }

        var rowQuery = baseQuery.Select(m => new VideoRow
        {
            ItemId = m.Id,
            Title = m.Title,
            ShowTitle = null,
            SeasonNumber = null,
            EpisodeNumber = null,
            ReleaseYear = m.ReleaseDate != null ? m.ReleaseDate.Value.Year : (int?)null,
            ContentRating = m.ContentRating,
            DurationSeconds = m.Analysis != null && m.Analysis.Duration != null ? (int?)(int)m.Analysis.Duration.Value.TotalSeconds : null,
            AddedAt = m.AddedAt,
            LibraryId = m.LibraryId,
            Genres = m.Genres.Select(g => g.Name.ToLower()),
            ServerAdminRating = m.ServerAdminRating,
            AudienceRating = m.ThirdPartyRating1,
            MyRating = _context.UserMediaRatings
                .Where(r => r.ProfileId == profileId && r.MediaItemId == m.Id)
                .Select(r => (decimal?)r.Rating)
                .FirstOrDefault(),
            IsWatched = _context.UserMediaStates.Any(s => s.ProfileId == profileId && s.MediaItemId == m.Id && s.IsPlayed),
            LastPlayedAt = _context.UserMediaStates
                .Where(s => s.ProfileId == profileId && s.MediaItemId == m.Id)
                .Max(s => (DateTime?)s.LastPlayedAt)
        });

        var predicate = BuildGroupPredicate<VideoRow>(definition.Root, VideoFieldAccessor);
        if (predicate != null) rowQuery = rowQuery.Where(predicate);

        return rowQuery;
    }

    private IQueryable<VideoRow> BuildEpisodeRowQuery(SmartPlaylistDefinition definition, Guid profileId, MusicAccessFilter access)
    {
        var baseQuery = _context.Episodes
            .AsNoTracking()
            .Where(e => e.MissingSince == null)
            .Where(e => e.Season != null && e.Season.TvShow != null);

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            baseQuery = baseQuery.Where(e => allowed.Contains(e.LibraryId));
        }

        if (!access.HasAllRatings)
        {
            var allowedRatings = access.AllowedRatings;
            if (access.BlockUnratedContent)
                baseQuery = baseQuery.Where(e => e.ContentRating != null && allowedRatings.Contains(e.ContentRating));
            else
                baseQuery = baseQuery.Where(e => e.ContentRating == null || allowedRatings.Contains(e.ContentRating));
        }

        var rowQuery = baseQuery.Select(e => new VideoRow
        {
            ItemId = e.Id,
            Title = e.Title,
            ShowTitle = e.Season.TvShow.Title,
            SeasonNumber = e.Season.SeasonNumber,
            EpisodeNumber = e.EpisodeNumber,
            ReleaseYear = e.ReleaseDate != null ? e.ReleaseDate.Value.Year : (int?)null,
            ContentRating = e.ContentRating,
            DurationSeconds = e.Analysis != null && e.Analysis.Duration != null ? (int?)(int)e.Analysis.Duration.Value.TotalSeconds : null,
            AddedAt = e.AddedAt,
            LibraryId = e.LibraryId,
            Genres = e.Season.TvShow.Genres.Select(g => g.Name.ToLower()),
            ServerAdminRating = e.ServerAdminRating,
            AudienceRating = e.ThirdPartyRating1,
            MyRating = _context.UserMediaRatings
                .Where(r => r.ProfileId == profileId && r.MediaItemId == e.Id)
                .Select(r => (decimal?)r.Rating)
                .FirstOrDefault(),
            IsWatched = _context.UserMediaStates.Any(s => s.ProfileId == profileId && s.MediaItemId == e.Id && s.IsPlayed),
            LastPlayedAt = _context.UserMediaStates
                .Where(s => s.ProfileId == profileId && s.MediaItemId == e.Id)
                .Max(s => (DateTime?)s.LastPlayedAt)
        });

        var predicate = BuildGroupPredicate<VideoRow>(definition.Root, VideoFieldAccessor);
        if (predicate != null) rowQuery = rowQuery.Where(predicate);

        return rowQuery;
    }

    private static IOrderedQueryable<MusicRow> ApplyMusicSort(IQueryable<MusicRow> query, SmartPlaylistSortBy sortBy, SmartPlaylistSortDirection dir)
    {
        bool desc = dir == SmartPlaylistSortDirection.Desc;
        return sortBy switch
        {
            SmartPlaylistSortBy.Random => query.OrderBy(r => EF.Functions.Random()),
            SmartPlaylistSortBy.Title => desc ? query.OrderByDescending(r => r.Title) : query.OrderBy(r => r.Title),
            SmartPlaylistSortBy.ArtistName => desc ? query.OrderByDescending(r => r.ArtistName ?? r.Artist ?? "") : query.OrderBy(r => r.ArtistName ?? r.Artist ?? ""),
            SmartPlaylistSortBy.AlbumTitle => desc ? query.OrderByDescending(r => r.AlbumTitle ?? "") : query.OrderBy(r => r.AlbumTitle ?? ""),
            SmartPlaylistSortBy.Year => desc ? query.OrderByDescending(r => r.AlbumYear) : query.OrderBy(r => r.AlbumYear),
            SmartPlaylistSortBy.DateAdded => desc ? query.OrderByDescending(r => r.AddedAt) : query.OrderBy(r => r.AddedAt),
            SmartPlaylistSortBy.LastPlayedAt => desc ? query.OrderByDescending(r => r.LastPlayedAt) : query.OrderBy(r => r.LastPlayedAt),
            SmartPlaylistSortBy.PlayCount => desc ? query.OrderByDescending(r => r.PlayCount) : query.OrderBy(r => r.PlayCount),
            SmartPlaylistSortBy.DurationSeconds => desc ? query.OrderByDescending(r => r.DurationSeconds) : query.OrderBy(r => r.DurationSeconds),
            _ => query.OrderBy(r => r.Title)
        };
    }

    private static IOrderedQueryable<VideoRow> ApplyVideoSort(IQueryable<VideoRow> query, SmartPlaylistSortBy sortBy, SmartPlaylistSortDirection dir)
    {
        bool desc = dir == SmartPlaylistSortDirection.Desc;
        return sortBy switch
        {
            SmartPlaylistSortBy.Random => query.OrderBy(r => EF.Functions.Random()),
            SmartPlaylistSortBy.Title => desc ? query.OrderByDescending(r => r.Title) : query.OrderBy(r => r.Title),
            SmartPlaylistSortBy.Year => desc ? query.OrderByDescending(r => r.ReleaseYear) : query.OrderBy(r => r.ReleaseYear),
            SmartPlaylistSortBy.DateAdded => desc ? query.OrderByDescending(r => r.AddedAt) : query.OrderBy(r => r.AddedAt),
            SmartPlaylistSortBy.LastPlayedAt => desc ? query.OrderByDescending(r => r.LastPlayedAt) : query.OrderBy(r => r.LastPlayedAt),
            SmartPlaylistSortBy.DurationSeconds => desc ? query.OrderByDescending(r => r.DurationSeconds) : query.OrderBy(r => r.DurationSeconds),
            _ => query.OrderBy(r => r.Title)
        };
    }

    private delegate FieldAccess? FieldAccessor(ParameterExpression param, SmartPlaylistField field);

    private sealed class FieldAccess
    {
        public required Expression Member { get; init; }
        public required Kind FieldKind { get; init; }
        public Expression? FallbackMember { get; init; }
        public Expression? CollectionMember { get; init; }

        public enum Kind { String, Int, Date, Bool, Decimal, Guid, StringCollection }
    }

    private static FieldAccess? MusicFieldAccessor(ParameterExpression p, SmartPlaylistField field) => field switch
    {
        SmartPlaylistField.Title => new() { Member = Expression.Property(p, nameof(MusicRow.Title)), FieldKind = FieldAccess.Kind.String },
        SmartPlaylistField.Artist => new() { Member = Expression.Property(p, nameof(MusicRow.Artist)), FieldKind = FieldAccess.Kind.String, FallbackMember = Expression.Property(p, nameof(MusicRow.ArtistName)) },
        SmartPlaylistField.AlbumTitle => new() { Member = Expression.Property(p, nameof(MusicRow.AlbumTitle)), FieldKind = FieldAccess.Kind.String },
        SmartPlaylistField.AlbumArtist => new() { Member = Expression.Property(p, nameof(MusicRow.AlbumArtist)), FieldKind = FieldAccess.Kind.String },
        SmartPlaylistField.Genre => new() { Member = Expression.Property(p, nameof(MusicRow.AlbumGenre)), FieldKind = FieldAccess.Kind.String },
        SmartPlaylistField.ContentRating => new() { Member = Expression.Property(p, nameof(MusicRow.ContentRating)), FieldKind = FieldAccess.Kind.String },
        SmartPlaylistField.Year => new() { Member = Expression.Property(p, nameof(MusicRow.AlbumYear)), FieldKind = FieldAccess.Kind.Int },
        SmartPlaylistField.DurationSeconds => new() { Member = Expression.Property(p, nameof(MusicRow.DurationSeconds)), FieldKind = FieldAccess.Kind.Int },
        SmartPlaylistField.PlayCount => new() { Member = Expression.Property(p, nameof(MusicRow.PlayCount)), FieldKind = FieldAccess.Kind.Int },
        SmartPlaylistField.TrackNumber => new() { Member = Expression.Property(p, nameof(MusicRow.TrackNumber)), FieldKind = FieldAccess.Kind.Int },
        SmartPlaylistField.DiscNumber => new() { Member = Expression.Property(p, nameof(MusicRow.DiscNumber)), FieldKind = FieldAccess.Kind.Int },
        SmartPlaylistField.LastPlayedAt => new() { Member = Expression.Property(p, nameof(MusicRow.LastPlayedAt)), FieldKind = FieldAccess.Kind.Date },
        SmartPlaylistField.DateAdded => new() { Member = Expression.Property(p, nameof(MusicRow.AddedAt)), FieldKind = FieldAccess.Kind.Date },
        SmartPlaylistField.Liked => new() { Member = Expression.Property(p, nameof(MusicRow.Liked)), FieldKind = FieldAccess.Kind.Bool },
        SmartPlaylistField.IsCompilation => new() { Member = Expression.Property(p, nameof(MusicRow.IsCompilation)), FieldKind = FieldAccess.Kind.Bool },
        SmartPlaylistField.LibraryId => new() { Member = Expression.Property(p, nameof(MusicRow.LibraryId)), FieldKind = FieldAccess.Kind.Guid },
        _ => null
    };

    private static FieldAccess? VideoFieldAccessor(ParameterExpression p, SmartPlaylistField field) => field switch
    {
        SmartPlaylistField.Title => new() { Member = Expression.Property(p, nameof(VideoRow.Title)), FieldKind = FieldAccess.Kind.String },
        SmartPlaylistField.ShowTitle => new() { Member = Expression.Property(p, nameof(VideoRow.ShowTitle)), FieldKind = FieldAccess.Kind.String },
        SmartPlaylistField.Genre => new() { Member = Expression.Property(p, nameof(VideoRow.Genres)), FieldKind = FieldAccess.Kind.StringCollection, CollectionMember = Expression.Property(p, nameof(VideoRow.Genres)) },
        SmartPlaylistField.ContentRating => new() { Member = Expression.Property(p, nameof(VideoRow.ContentRating)), FieldKind = FieldAccess.Kind.String },
        SmartPlaylistField.ReleaseYear => new() { Member = Expression.Property(p, nameof(VideoRow.ReleaseYear)), FieldKind = FieldAccess.Kind.Int },
        SmartPlaylistField.Year => new() { Member = Expression.Property(p, nameof(VideoRow.ReleaseYear)), FieldKind = FieldAccess.Kind.Int },
        SmartPlaylistField.SeasonNumber => new() { Member = Expression.Property(p, nameof(VideoRow.SeasonNumber)), FieldKind = FieldAccess.Kind.Int },
        SmartPlaylistField.EpisodeNumber => new() { Member = Expression.Property(p, nameof(VideoRow.EpisodeNumber)), FieldKind = FieldAccess.Kind.Int },
        SmartPlaylistField.DurationSeconds => new() { Member = Expression.Property(p, nameof(VideoRow.DurationSeconds)), FieldKind = FieldAccess.Kind.Int },
        SmartPlaylistField.LastPlayedAt => new() { Member = Expression.Property(p, nameof(VideoRow.LastPlayedAt)), FieldKind = FieldAccess.Kind.Date },
        SmartPlaylistField.DateAdded => new() { Member = Expression.Property(p, nameof(VideoRow.AddedAt)), FieldKind = FieldAccess.Kind.Date },
        SmartPlaylistField.IsWatched => new() { Member = Expression.Property(p, nameof(VideoRow.IsWatched)), FieldKind = FieldAccess.Kind.Bool },
        SmartPlaylistField.Rating => new() { Member = Expression.Property(p, nameof(VideoRow.ServerAdminRating)), FieldKind = FieldAccess.Kind.Decimal },
        SmartPlaylistField.ServerAdminRating => new() { Member = Expression.Property(p, nameof(VideoRow.ServerAdminRating)), FieldKind = FieldAccess.Kind.Decimal },
        SmartPlaylistField.MyRating => new() { Member = Expression.Property(p, nameof(VideoRow.MyRating)), FieldKind = FieldAccess.Kind.Decimal },
        SmartPlaylistField.AudienceRating => new() { Member = Expression.Property(p, nameof(VideoRow.AudienceRating)), FieldKind = FieldAccess.Kind.Decimal },
        SmartPlaylistField.LibraryId => new() { Member = Expression.Property(p, nameof(VideoRow.LibraryId)), FieldKind = FieldAccess.Kind.Guid },
        _ => null
    };

    private static Expression<Func<T, bool>>? BuildGroupPredicate<T>(SmartPlaylistRuleGroup? group, FieldAccessor accessor)
    {
        if (group == null) return null;

        var parts = new List<Expression<Func<T, bool>>>();

        foreach (var rule in group.Rules)
        {
            var rulePred = BuildRulePredicate<T>(rule, accessor);
            if (rulePred != null) parts.Add(rulePred);
        }

        foreach (var sub in group.Groups)
        {
            var subPred = BuildGroupPredicate<T>(sub, accessor);
            if (subPred != null) parts.Add(subPred);
        }

        if (parts.Count == 0) return null;

        if (group.Match == SmartPlaylistMatch.Any) return parts.Aggregate(OrElse);
        return parts.Aggregate(AndAlso);
    }

    private static Expression<Func<T, bool>>? BuildRulePredicate<T>(SmartPlaylistRule rule, FieldAccessor accessor)
    {
        var param = Expression.Parameter(typeof(T), "r");
        var access = accessor(param, rule.Field);
        if (access == null) return null;

        Expression? body = access.FieldKind switch
        {
            FieldAccess.Kind.String => BuildStringRule(access.Member, access.FallbackMember, rule),
            FieldAccess.Kind.Int => BuildIntRule(access.Member, rule),
            FieldAccess.Kind.Date => BuildDateRule(access.Member, rule),
            FieldAccess.Kind.Bool => BuildBoolRule(access.Member, rule),
            FieldAccess.Kind.Decimal => BuildDecimalRule(access.Member, rule),
            FieldAccess.Kind.Guid => BuildGuidRule(access.Member, rule),
            FieldAccess.Kind.StringCollection => BuildStringCollectionRule(access.CollectionMember!, rule),
            _ => null
        };

        if (body == null) return null;
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    private static Expression? BuildStringRule(Expression member, Expression? fallback, SmartPlaylistRule rule)
    {
        var value = rule.Value ?? string.Empty;

        Expression target = fallback != null
            ? Expression.Coalesce(member, Expression.Coalesce(fallback, Expression.Constant(string.Empty)))
            : Expression.Coalesce(member, Expression.Constant(string.Empty));

        var valueExpr = Expression.Constant(value, typeof(string));

        switch (rule.Operator)
        {
            case SmartPlaylistOperator.Equals:
                return Expression.Equal(Expression.Call(target, "ToLower", null), Expression.Call(valueExpr, "ToLower", null));
            case SmartPlaylistOperator.NotEquals:
                return Expression.NotEqual(Expression.Call(target, "ToLower", null), Expression.Call(valueExpr, "ToLower", null));
            case SmartPlaylistOperator.Contains:
            {
                var contains = typeof(string).GetMethod("Contains", new[] { typeof(string) })!;
                return Expression.Call(Expression.Call(target, "ToLower", null), contains, Expression.Call(valueExpr, "ToLower", null));
            }
            case SmartPlaylistOperator.NotContains:
            {
                var contains = typeof(string).GetMethod("Contains", new[] { typeof(string) })!;
                return Expression.Not(Expression.Call(Expression.Call(target, "ToLower", null), contains, Expression.Call(valueExpr, "ToLower", null)));
            }
            case SmartPlaylistOperator.StartsWith:
            {
                var starts = typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!;
                return Expression.Call(Expression.Call(target, "ToLower", null), starts, Expression.Call(valueExpr, "ToLower", null));
            }
            case SmartPlaylistOperator.EndsWith:
            {
                var ends = typeof(string).GetMethod("EndsWith", new[] { typeof(string) })!;
                return Expression.Call(Expression.Call(target, "ToLower", null), ends, Expression.Call(valueExpr, "ToLower", null));
            }
            case SmartPlaylistOperator.IsNull:
                return Expression.Equal(member, Expression.Constant(null, member.Type));
            case SmartPlaylistOperator.IsNotNull:
                return Expression.NotEqual(member, Expression.Constant(null, member.Type));
            default:
                return null;
        }
    }

    private static Expression? BuildStringCollectionRule(Expression collection, SmartPlaylistRule rule)
    {
        var value = (rule.Value ?? string.Empty).ToLowerInvariant();
        var valueExpr = Expression.Constant(value, typeof(string));
        var stringContains = typeof(string).GetMethod("Contains", new[] { typeof(string) })!;
        var p = Expression.Parameter(typeof(string), "g");
        Expression elementBody;
        switch (rule.Operator)
        {
            case SmartPlaylistOperator.Equals:
                elementBody = Expression.Equal(p, valueExpr);
                break;
            case SmartPlaylistOperator.Contains:
                elementBody = Expression.Call(p, stringContains, valueExpr);
                break;
            case SmartPlaylistOperator.StartsWith:
                elementBody = Expression.Call(p, typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!, valueExpr);
                break;
            case SmartPlaylistOperator.EndsWith:
                elementBody = Expression.Call(p, typeof(string).GetMethod("EndsWith", new[] { typeof(string) })!, valueExpr);
                break;
            default:
                return null;
        }
        var lambda = Expression.Lambda<Func<string, bool>>(elementBody, p);
        var anyMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == "Any" && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(string));
        var anyCall = Expression.Call(anyMethod, collection, lambda);
        return rule.Operator == SmartPlaylistOperator.NotContains ? Expression.Not(anyCall) : anyCall;
    }

    private static Expression? BuildIntRule(Expression member, SmartPlaylistRule rule)
    {
        var memberType = member.Type;
        bool nullable = Nullable.GetUnderlyingType(memberType) != null;

        if (rule.Operator == SmartPlaylistOperator.IsNull)
        {
            if (!nullable) return Expression.Constant(false);
            return Expression.Equal(member, Expression.Constant(null, memberType));
        }
        if (rule.Operator == SmartPlaylistOperator.IsNotNull)
        {
            if (!nullable) return Expression.Constant(true);
            return Expression.NotEqual(member, Expression.Constant(null, memberType));
        }

        if (!int.TryParse(rule.Value, out var v)) return null;
        Expression valueExpr = nullable ? Expression.Constant((int?)v, typeof(int?)) : Expression.Constant(v, typeof(int));

        switch (rule.Operator)
        {
            case SmartPlaylistOperator.Equals: return Expression.Equal(member, valueExpr);
            case SmartPlaylistOperator.NotEquals: return Expression.NotEqual(member, valueExpr);
            case SmartPlaylistOperator.GreaterThan: return Expression.GreaterThan(member, valueExpr);
            case SmartPlaylistOperator.LessThan: return Expression.LessThan(member, valueExpr);
            case SmartPlaylistOperator.Between:
                if (!int.TryParse(rule.SecondValue, out var v2)) return null;
                Expression secondExpr = nullable ? Expression.Constant((int?)v2, typeof(int?)) : Expression.Constant(v2, typeof(int));
                return Expression.AndAlso(
                    Expression.GreaterThanOrEqual(member, valueExpr),
                    Expression.LessThanOrEqual(member, secondExpr)
                );
            default: return null;
        }
    }

    private static Expression? BuildDecimalRule(Expression member, SmartPlaylistRule rule)
    {
        var memberType = member.Type;
        bool nullable = Nullable.GetUnderlyingType(memberType) != null;

        if (rule.Operator == SmartPlaylistOperator.IsNull)
        {
            if (!nullable) return Expression.Constant(false);
            return Expression.Equal(member, Expression.Constant(null, memberType));
        }
        if (rule.Operator == SmartPlaylistOperator.IsNotNull)
        {
            if (!nullable) return Expression.Constant(true);
            return Expression.NotEqual(member, Expression.Constant(null, memberType));
        }

        if (!decimal.TryParse(rule.Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v)) return null;
        Expression valueExpr = nullable ? Expression.Constant((decimal?)v, typeof(decimal?)) : Expression.Constant(v, typeof(decimal));

        switch (rule.Operator)
        {
            case SmartPlaylistOperator.Equals: return Expression.Equal(member, valueExpr);
            case SmartPlaylistOperator.NotEquals: return Expression.NotEqual(member, valueExpr);
            case SmartPlaylistOperator.GreaterThan: return Expression.GreaterThan(member, valueExpr);
            case SmartPlaylistOperator.LessThan: return Expression.LessThan(member, valueExpr);
            case SmartPlaylistOperator.Between:
                if (!decimal.TryParse(rule.SecondValue, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v2)) return null;
                Expression secondExpr = nullable ? Expression.Constant((decimal?)v2, typeof(decimal?)) : Expression.Constant(v2, typeof(decimal));
                return Expression.AndAlso(
                    Expression.GreaterThanOrEqual(member, valueExpr),
                    Expression.LessThanOrEqual(member, secondExpr)
                );
            default: return null;
        }
    }

    private static Expression? BuildDateRule(Expression member, SmartPlaylistRule rule)
    {
        var memberType = member.Type;
        bool nullable = Nullable.GetUnderlyingType(memberType) != null;

        if (rule.Operator == SmartPlaylistOperator.IsNull)
        {
            if (!nullable) return Expression.Constant(false);
            return Expression.Equal(member, Expression.Constant(null, memberType));
        }
        if (rule.Operator == SmartPlaylistOperator.IsNotNull)
        {
            if (!nullable) return Expression.Constant(true);
            return Expression.NotEqual(member, Expression.Constant(null, memberType));
        }

        if (rule.Operator == SmartPlaylistOperator.InLastDays || rule.Operator == SmartPlaylistOperator.NotInLastDays)
        {
            if (!int.TryParse(rule.Value, out var days) || days <= 0) return null;
            var cutoff = DateTime.UtcNow.AddDays(-days);
            Expression cutoffExpr = nullable ? Expression.Constant((DateTime?)cutoff, typeof(DateTime?)) : Expression.Constant(cutoff, typeof(DateTime));
            var inLast = Expression.GreaterThanOrEqual(member, cutoffExpr);
            return rule.Operator == SmartPlaylistOperator.InLastDays ? inLast : Expression.Not(inLast);
        }

        if (!DateTime.TryParse(rule.Value, out var dt)) return null;
        var dtUtc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        Expression dtExpr = nullable ? Expression.Constant((DateTime?)dtUtc, typeof(DateTime?)) : Expression.Constant(dtUtc, typeof(DateTime));

        switch (rule.Operator)
        {
            case SmartPlaylistOperator.Equals: return Expression.Equal(member, dtExpr);
            case SmartPlaylistOperator.NotEquals: return Expression.NotEqual(member, dtExpr);
            case SmartPlaylistOperator.GreaterThan: return Expression.GreaterThan(member, dtExpr);
            case SmartPlaylistOperator.LessThan: return Expression.LessThan(member, dtExpr);
            default: return null;
        }
    }

    private static Expression? BuildBoolRule(Expression member, SmartPlaylistRule rule)
    {
        bool target = rule.Value != null && (rule.Value.Equals("true", StringComparison.OrdinalIgnoreCase) || rule.Value == "1");
        var valueExpr = Expression.Constant(target);
        return rule.Operator switch
        {
            SmartPlaylistOperator.Equals => Expression.Equal(member, valueExpr),
            SmartPlaylistOperator.NotEquals => Expression.NotEqual(member, valueExpr),
            _ => null
        };
    }

    private static Expression? BuildGuidRule(Expression member, SmartPlaylistRule rule)
    {
        if (!Guid.TryParse(rule.Value, out var g)) return null;
        var valueExpr = Expression.Constant(g, typeof(Guid));
        return rule.Operator switch
        {
            SmartPlaylistOperator.Equals => Expression.Equal(member, valueExpr),
            SmartPlaylistOperator.NotEquals => Expression.NotEqual(member, valueExpr),
            _ => null
        };
    }

    private static Expression<Func<T, bool>> AndAlso<T>(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var aBody = new ParamReplacer(a.Parameters[0], param).Visit(a.Body)!;
        var bBody = new ParamReplacer(b.Parameters[0], param).Visit(b.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(aBody, bBody), param);
    }

    private static Expression<Func<T, bool>> OrElse<T>(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var aBody = new ParamReplacer(a.Parameters[0], param).Visit(a.Body)!;
        var bBody = new ParamReplacer(b.Parameters[0], param).Visit(b.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(aBody, bBody), param);
    }

    private sealed class ParamReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;
        public ParamReplacer(ParameterExpression from, ParameterExpression to) { _from = from; _to = to; }
        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : base.VisitParameter(node);
    }

    private sealed class MusicRow
    {
        public Guid TrackId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Artist { get; set; }
        public Guid? AlbumId { get; set; }
        public string? AlbumTitle { get; set; }
        public string? AlbumArtist { get; set; }
        public int? AlbumYear { get; set; }
        public string? AlbumGenre { get; set; }
        public bool IsCompilation { get; set; }
        public Guid ArtistId { get; set; }
        public string? ArtistName { get; set; }
        public Guid LibraryId { get; set; }
        public string? ContentRating { get; set; }
        public int TrackNumber { get; set; }
        public int? DiscNumber { get; set; }
        public int? DurationSeconds { get; set; }
        public DateTime AddedAt { get; set; }
        public int PlayCount { get; set; }
        public DateTime? LastPlayedAt { get; set; }
        public bool Liked { get; set; }
    }

    private sealed class VideoRow
    {
        public Guid ItemId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? ShowTitle { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public int? ReleaseYear { get; set; }
        public string? ContentRating { get; set; }
        public int? DurationSeconds { get; set; }
        public DateTime AddedAt { get; set; }
        public Guid LibraryId { get; set; }
        public IEnumerable<string> Genres { get; set; } = Array.Empty<string>();
        public decimal? ServerAdminRating { get; set; }
        public decimal? MyRating { get; set; }
        public decimal? AudienceRating { get; set; }
        public bool IsWatched { get; set; }
        public DateTime? LastPlayedAt { get; set; }
    }
}
