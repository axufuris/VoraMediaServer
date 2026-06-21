using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Extensions;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Media.Requests;
using Vora.Application.Media.ViewModels;
using Vora.Application.Search.ViewModels;
using Vora.Application.Streaming;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;

namespace Vora.Api.Endpoints;

public static class MusicEndpoints
{
    public static IEndpointRouteBuilder MapMusicEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/music").WithTags("Music");

        group.MapGet("/artists", GetArtistsAsync)
            .RequireAuthorization()
            .WithName("ListArtists")
            .Produces<IEnumerable<ArtistVM>>(StatusCodes.Status200OK);
        group.MapGet("/artists/{artistId:guid}", GetArtistDetailAsync)
            .RequireAuthorization()
            .WithName("GetArtistDetail")
            .Produces<ArtistDetailVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/artists/{artistId:guid}/tracks", GetArtistTracksAsync)
            .RequireAuthorization()
            .WithName("ListArtistTracks")
            .Produces<IEnumerable<ArtistTrackVM>>(StatusCodes.Status200OK);
        group.MapGet("/albums/{albumId:guid}", GetAlbumDetailAsync)
            .RequireAuthorization()
            .WithName("GetAlbumDetail")
            .Produces<AlbumDetailVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/artists/{artistId:guid}", UpdateArtistAsync).RequireAuthorization();
        group.MapPut("/albums/{albumId:guid}", UpdateAlbumAsync).RequireAuthorization();
        group.MapPut("/tracks/{trackId:guid}", UpdateTrackAsync).RequireAuthorization();

        group.MapPost("/artists/{artistId:guid}/artwork/upload", UploadArtistArtworkAsync).RequireAuthorization().DisableAntiforgery();
        group.MapPost("/albums/{albumId:guid}/artwork/upload", UploadAlbumArtworkAsync).RequireAuthorization().DisableAntiforgery();
        group.MapPost("/artists/{artistId:guid}/background/upload", UploadArtistBackgroundAsync).RequireAuthorization().DisableAntiforgery();
        group.MapPost("/albums/{albumId:guid}/background/upload", UploadAlbumBackgroundAsync).RequireAuthorization().DisableAntiforgery();
        group.MapPost("/artists/{artistId:guid}/banner/upload", UploadArtistBannerAsync).RequireAuthorization().DisableAntiforgery();
        group.MapPost("/artists/{artistId:guid}/clearlogo/upload", UploadArtistClearLogoAsync).RequireAuthorization().DisableAntiforgery();
        group.MapPost("/albums/{albumId:guid}/discart/upload", UploadAlbumDiscArtAsync).RequireAuthorization().DisableAntiforgery();

        group.MapGet("/artists/{artistId:guid}/artwork/suggestions", GetArtistArtworkSuggestionsAsync).RequireAuthorization();
        group.MapGet("/albums/{albumId:guid}/artwork/suggestions", GetAlbumArtworkSuggestionsAsync).RequireAuthorization();

        group.MapPost("/artists/{artistId:guid}/artwork/refresh", RefreshArtistArtworkAsync).RequireAuthorization();
        group.MapPost("/albums/{albumId:guid}/artwork/refresh", RefreshAlbumArtworkAsync).RequireAuthorization();

        group.MapGet("/tracks/{trackId:guid}/stream", StreamTrackAsync).AllowAnonymous();

        group.MapGet("/search", SearchMusicAsync)
            .RequireAuthorization()
            .WithName("SearchMusic")
            .Produces<IEnumerable<MusicSearchResultVM>>(StatusCodes.Status200OK);

        group.MapPost("/tracks/{trackId:guid}/like", LikeTrackAsync)
            .RequireAuthorization()
            .WithName("LikeTrack")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
        group.MapDelete("/tracks/{trackId:guid}/like", UnlikeTrackAsync)
            .RequireAuthorization()
            .WithName("UnlikeTrack")
            .Produces(StatusCodes.Status204NoContent);
        group.MapGet("/likes", GetLikedTracksAsync)
            .RequireAuthorization()
            .WithName("GetLikedTracks")
            .Produces<LikedTracksVM>(StatusCodes.Status200OK);

        group.MapPut("/albums/{albumId:guid}/rating", SetAlbumRatingAsync)
            .RequireAuthorization()
            .WithName("SetAlbumRating")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);
        group.MapPut("/artists/{artistId:guid}/rating", SetArtistRatingAsync)
            .RequireAuthorization()
            .WithName("SetArtistRating")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/tracks/{trackId:guid}/lyrics", GetTrackLyricsAsync).RequireAuthorization();

        group.MapPost("/tracks/{trackId:guid}/played", RecordTrackPlayAsync).RequireAuthorization();
        group.MapPost("/tracks/{trackId:guid}/now-playing", UpdateNowPlayingAsync).RequireAuthorization();
        group.MapGet("/history/recent", GetRecentlyPlayedAsync)
            .RequireAuthorization()
            .WithName("ListRecentlyPlayedTracks")
            .Produces<IEnumerable<ArtistTrackVM>>(StatusCodes.Status200OK);
        group.MapGet("/history/top-tracks", GetTopTracksAsync)
            .RequireAuthorization()
            .WithName("ListTopTracks")
            .Produces<IEnumerable<ArtistTrackVM>>(StatusCodes.Status200OK);
        group.MapGet("/history/top-artists", GetTopArtistsAsync)
            .RequireAuthorization()
            .WithName("ListTopArtists")
            .Produces<IEnumerable<ArtistVM>>(StatusCodes.Status200OK);
        group.MapGet("/albums/recent", GetRecentlyAddedAlbumsAsync)
            .RequireAuthorization()
            .WithName("ListRecentAlbums")
            .Produces<IEnumerable<AlbumVM>>(StatusCodes.Status200OK);

        group.MapPost("/lastfm/auth/start", StartLastFmAuthAsync).RequireAuthorization()
            .Produces<LastFmAuthStartVM>(StatusCodes.Status200OK);
        group.MapPost("/lastfm/auth/complete", CompleteLastFmAuthAsync).RequireAuthorization();
        group.MapDelete("/lastfm/auth", DisconnectLastFmAsync).RequireAuthorization();

        group.MapGet("/recommendations/mixes", GetMixesAsync)
            .RequireAuthorization()
            .WithName("ListProfileMixes")
            .Produces<IEnumerable<GeneratedMixSummaryVM>>(StatusCodes.Status200OK);
        group.MapGet("/recommendations/mixes/{mixId:guid}", GetMixDetailAsync)
            .RequireAuthorization()
            .WithName("GetMixDetail")
            .Produces<GeneratedMixDetailVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/recommendations/because-you-played", GetBecauseYouPlayedAsync)
            .RequireAuthorization()
            .WithName("ListBecauseYouPlayed")
            .Produces<IEnumerable<BecauseYouPlayedRowVM>>(StatusCodes.Status200OK);
        group.MapPost("/recommendations/refresh", RefreshRecommendationsAsync).RequireAuthorization();

        group.MapPost("/recommendations/radio", StartRadioAsync).RequireAuthorization();
        group.MapPost("/recommendations/radio/extend", ExtendRadioAsync).RequireAuthorization();

        group.MapGet("/stations", GetStationsAsync).RequireAuthorization();
        group.MapPost("/stations", CreateStationAsync).RequireAuthorization();
        group.MapDelete("/stations/{stationId:guid}", DeleteStationAsync).RequireAuthorization();
        group.MapPost("/stations/{stationId:guid}/play", TouchStationAsync).RequireAuthorization();

        group.MapGet("/recommendations/year-recap", GetYearRecapAsync)
            .RequireAuthorization()
            .WithName("GetYearRecap")
            .Produces<YearRecapVM>(StatusCodes.Status200OK);
        group.MapGet("/recommendations/years", GetYearsWithHistoryAsync)
            .RequireAuthorization()
            .WithName("ListYearsWithHistory")
            .Produces<IEnumerable<int>>(StatusCodes.Status200OK);

        group.MapGet("/artists/{artistId:guid}/similar", GetSimilarArtistsAsync).RequireAuthorization();

        group.MapGet("/genres", GetGenresAsync)
            .RequireAuthorization()
            .WithName("ListGenres")
            .Produces<IEnumerable<GenreSummaryVM>>(StatusCodes.Status200OK);
        group.MapGet("/genres/{genre}", GetGenreContentAsync)
            .RequireAuthorization()
            .WithName("GetGenreContent")
            .Produces<GenreContentVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/playback/heartbeat", HeartbeatAsync).RequireAuthorization();
        group.MapPost("/playback/stop", StopPlaybackAsync).RequireAuthorization();
        group.MapGet("/playback/active", GetActivePlaybackAsync).RequireAuthorization();

        var adminGroup = routes.MapGroup("/api/admin/music").WithTags("AdminMusic");
        adminGroup.MapGet("/history", GetAdminMusicHistoryAsync).RequireAuthorization();
        adminGroup.MapGet("/summary", GetAdminMusicSummaryAsync).RequireAuthorization();

        return routes;
    }

    private static async Task<IResult> GetAdminMusicHistoryAsync(
        [FromQuery] Guid? profileId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        ClaimsPrincipal user,
        IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        var result = await manager.GetAdminMusicHistoryAsync(
            profileId,
            from,
            to,
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            page ?? 1,
            pageSize ?? 50);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAdminMusicSummaryAsync(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        ClaimsPrincipal user,
        IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        var summary = await manager.GetAdminMusicSummaryAsync(from, to);
        return Results.Ok(summary);
    }

    private static async Task<IResult> SearchMusicAsync([FromQuery] string? q, [FromQuery] int? limit, ClaimsPrincipal user, IMusicManager manager)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Results.Ok(Array.Empty<object>());
        }
        var results = await manager.SearchAsync(q.Trim(), BuildFilter(user), limit ?? 30);
        return Results.Ok(results);
    }

    private static async Task<IResult> RefreshArtistArtworkAsync(Guid artistId, [FromQuery] bool? force, ClaimsPrincipal user, IMusicManager manager, CancellationToken cancellationToken)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        var url = await manager.RefreshArtistArtworkFromProvidersAsync(artistId, force ?? true, cancellationToken);
        return Results.Ok(new { updated = url != null, artworkUrl = url });
    }

    private static async Task<IResult> RefreshAlbumArtworkAsync(Guid albumId, [FromQuery] bool? force, ClaimsPrincipal user, IMusicManager manager, CancellationToken cancellationToken)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        var url = await manager.RefreshAlbumArtworkFromProvidersAsync(albumId, force ?? true, cancellationToken);
        return Results.Ok(new { updated = url != null, artworkUrl = url });
    }

    private static async Task<IResult> UpdateArtistAsync(Guid artistId, [FromBody] UpdateArtistRequest request, ClaimsPrincipal user, IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        var ok = await manager.UpdateArtistAsync(artistId, request);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> UpdateAlbumAsync(Guid albumId, [FromBody] UpdateAlbumRequest request, ClaimsPrincipal user, IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        var ok = await manager.UpdateAlbumAsync(albumId, request);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> UpdateTrackAsync(Guid trackId, [FromBody] UpdateTrackRequest request, ClaimsPrincipal user, IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        var ok = await manager.UpdateTrackAsync(trackId, request);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> UploadArtistArtworkAsync(Guid artistId, IFormFile file, ClaimsPrincipal user, IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        if (file == null || file.Length == 0) return Results.BadRequest(new { error = "No file uploaded." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var url = await manager.SaveArtistArtworkAsync(artistId, ms.ToArray(), file.FileName);
        return url == null ? Results.NotFound() : Results.Ok(new { url });
    }

    private static async Task<IResult> UploadAlbumArtworkAsync(Guid albumId, IFormFile file, ClaimsPrincipal user, IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        if (file == null || file.Length == 0) return Results.BadRequest(new { error = "No file uploaded." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var url = await manager.SaveAlbumArtworkAsync(albumId, ms.ToArray(), file.FileName);
        return url == null ? Results.NotFound() : Results.Ok(new { url });
    }

    private static async Task<IResult> UploadArtistBackgroundAsync(Guid artistId, IFormFile file, ClaimsPrincipal user, IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        if (file == null || file.Length == 0) return Results.BadRequest(new { error = "No file uploaded." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var url = await manager.SaveArtistBackgroundAsync(artistId, ms.ToArray(), file.FileName);
        return url == null ? Results.NotFound() : Results.Ok(new { url });
    }

    private static async Task<IResult> UploadAlbumBackgroundAsync(Guid albumId, IFormFile file, ClaimsPrincipal user, IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        if (file == null || file.Length == 0) return Results.BadRequest(new { error = "No file uploaded." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var url = await manager.SaveAlbumBackgroundAsync(albumId, ms.ToArray(), file.FileName);
        return url == null ? Results.NotFound() : Results.Ok(new { url });
    }

    private static async Task<IResult> UploadArtistBannerAsync(Guid artistId, IFormFile file, ClaimsPrincipal user, IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        if (file == null || file.Length == 0) return Results.BadRequest(new { error = "No file uploaded." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var url = await manager.SaveArtistBannerAsync(artistId, ms.ToArray(), file.FileName);
        return url == null ? Results.NotFound() : Results.Ok(new { url });
    }

    private static async Task<IResult> UploadArtistClearLogoAsync(Guid artistId, IFormFile file, ClaimsPrincipal user, IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        if (file == null || file.Length == 0) return Results.BadRequest(new { error = "No file uploaded." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var url = await manager.SaveArtistClearLogoAsync(artistId, ms.ToArray(), file.FileName);
        return url == null ? Results.NotFound() : Results.Ok(new { url });
    }

    private static async Task<IResult> UploadAlbumDiscArtAsync(Guid albumId, IFormFile file, ClaimsPrincipal user, IMusicManager manager)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        if (file == null || file.Length == 0) return Results.BadRequest(new { error = "No file uploaded." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var url = await manager.SaveAlbumDiscArtAsync(albumId, ms.ToArray(), file.FileName);
        return url == null ? Results.NotFound() : Results.Ok(new { url });
    }

    private static async Task<IResult> GetAlbumArtworkSuggestionsAsync(Guid albumId, ClaimsPrincipal user, IMusicManager manager, CancellationToken cancellationToken)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        var results = await manager.GetAlbumArtworkSuggestionsAsync(albumId, cancellationToken);
        return Results.Ok(results);
    }

    private static async Task<IResult> GetArtistArtworkSuggestionsAsync(Guid artistId, ClaimsPrincipal user, IMusicManager manager, CancellationToken cancellationToken)
    {
        if (!user.IsAdmin()) return Results.Forbid();
        var results = await manager.GetArtistArtworkSuggestionsAsync(artistId, cancellationToken);
        return Results.Ok(results);
    }

    private static MusicAccessFilter BuildFilter(ClaimsPrincipal user) => new()
    {
        HasAllLibraryAccess = user.HasAllLibraryAccess(),
        AllowedLibraryIds = user.GetAllowedLibraryIds(),
        HasAllRatings = user.HasAllContentRatings(),
        AllowedRatings = user.GetAllowedMusicRatings(),
        BlockUnratedContent = user.BlockUnratedContent()
    };

    private static async Task<IResult> GetArtistsAsync([FromQuery] Guid? libraryId, [FromQuery] int? limit, ClaimsPrincipal user, IMusicManager manager)
    {
        var artists = await manager.GetArtistsAsync(libraryId, BuildFilter(user), limit);
        return Results.Ok(artists);
    }

    private static async Task<IResult> GetArtistDetailAsync(Guid artistId, ClaimsPrincipal user, IMusicManager manager)
    {
        var (artist, albums) = await manager.GetArtistDetailAsync(artistId, user.GetProfileId(), BuildFilter(user));
        if (artist == null) return Results.NotFound();
        return Results.Ok(new ArtistDetailVM { Artist = artist, Albums = albums });
    }

    private static async Task<IResult> GetAlbumDetailAsync(Guid albumId, ClaimsPrincipal user, IMusicManager manager)
    {
        var (album, tracks) = await manager.GetAlbumDetailAsync(albumId, user.GetProfileId(), BuildFilter(user));
        if (album == null) return Results.NotFound();
        return Results.Ok(new Vora.Application.Media.ViewModels.AlbumDetailVM { Album = album, Tracks = tracks });
    }

    private static async Task<IResult> GetArtistTracksAsync(Guid artistId, ClaimsPrincipal user, IMusicManager manager)
    {
        var tracks = await manager.GetTracksForArtistAsync(artistId, user.GetProfileId(), BuildFilter(user));
        return Results.Ok(tracks);
    }

    private static async Task<IResult> LikeTrackAsync(Guid trackId, ClaimsPrincipal user, IMusicManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        await manager.SetTrackLikedAsync(profileId.Value, trackId, liked: true);
        return Results.NoContent();
    }

    private static async Task<IResult> UnlikeTrackAsync(Guid trackId, ClaimsPrincipal user, IMusicManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        await manager.SetTrackLikedAsync(profileId.Value, trackId, liked: false);
        return Results.NoContent();
    }

    private static async Task<IResult> GetLikedTracksAsync(ClaimsPrincipal user, IMusicManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var tracks = await manager.GetLikedTracksAsync(profileId.Value, BuildFilter(user));
        return Results.Ok(new LikedTracksVM { Count = tracks.Count, Tracks = tracks });
    }

    public sealed class SetMusicRatingRequest
    {
        public decimal? Rating { get; set; }
    }

    private static async Task<IResult> SetAlbumRatingAsync(Guid albumId, [FromBody] SetMusicRatingRequest request, ClaimsPrincipal user, IMusicManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        try
        {
            var result = await manager.SetAlbumRatingAsync(profileId.Value, albumId, request.Rating, user.IsAdmin());
            if (!result.Found) return Results.NotFound(new { Message = "Album not found." });
            return Results.NoContent();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SetArtistRatingAsync(Guid artistId, [FromBody] SetMusicRatingRequest request, ClaimsPrincipal user, IMusicManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        try
        {
            var result = await manager.SetArtistRatingAsync(profileId.Value, artistId, request.Rating, user.IsAdmin());
            if (!result.Found) return Results.NotFound(new { Message = "Artist not found." });
            return Results.NoContent();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetTrackLyricsAsync(Guid trackId, ClaimsPrincipal user, IMusicManager manager, CancellationToken cancellationToken)
    {
        var lyrics = await manager.GetTrackLyricsAsync(trackId, BuildFilter(user), cancellationToken);
        if (lyrics == null) return Results.NotFound();
        return Results.Ok(new
        {
            plainLyrics = lyrics.PlainLyrics,
            syncedLyrics = lyrics.SyncedLyrics,
            isSynced = lyrics.IsSynced,
            providerName = lyrics.ProviderName,
            sourceUrl = lyrics.SourceUrl
        });
    }

    public class RecordPlayRequest
    {
        public int DurationListenedSeconds { get; set; }
        public bool Completed { get; set; }
    }

    private static async Task<IResult> RecordTrackPlayAsync(Guid trackId, [FromBody] RecordPlayRequest request, ClaimsPrincipal user, IMusicManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        await manager.RecordTrackPlayAsync(profileId.Value, trackId, request.DurationListenedSeconds, request.Completed);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateNowPlayingAsync(Guid trackId, ClaimsPrincipal user, IMusicManager manager, CancellationToken cancellationToken)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        await manager.UpdateNowPlayingAsync(profileId.Value, trackId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> StartLastFmAuthAsync(ClaimsPrincipal user, IMusicManager manager, CancellationToken cancellationToken)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var result = await manager.StartLastFmAuthAsync(cancellationToken);
        if (result == null) return Results.BadRequest(new { error = "Last.fm is not configured. An admin needs to set the API key + secret in the plugin settings." });
        return Results.Ok(new LastFmAuthStartVM(result.Token, result.AuthUrl));
    }

    private static async Task<IResult> CompleteLastFmAuthAsync([FromBody] CompleteLastFmAuthRequest request, ClaimsPrincipal user, IMusicManager manager, CancellationToken cancellationToken)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Token)) return Results.BadRequest();
        var username = await manager.CompleteLastFmAuthAsync(profileId.Value, request.Token, cancellationToken);
        if (username == null) return Results.BadRequest(new { error = "Authorization failed. Make sure you clicked Allow on Last.fm before completing." });
        return Results.Ok(new { username });
    }

    private static async Task<IResult> DisconnectLastFmAsync(ClaimsPrincipal user, IMusicManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        await manager.DisconnectLastFmAsync(profileId.Value);
        return Results.NoContent();
    }

    public sealed record CompleteLastFmAuthRequest(string Token);

    public sealed record LastFmAuthStartVM(string Token, string AuthUrl);

    private static async Task<IResult> GetRecentlyPlayedAsync([FromQuery] int? limit, ClaimsPrincipal user, IMusicManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var tracks = await manager.GetRecentlyPlayedAsync(profileId.Value, BuildFilter(user), limit ?? 20);
        return Results.Ok(tracks);
    }

    private static async Task<IResult> GetTopTracksAsync([FromQuery] int? limit, ClaimsPrincipal user, IMusicManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var tracks = await manager.GetTopPlayedTracksAsync(profileId.Value, BuildFilter(user), limit ?? 50);
        return Results.Ok(tracks);
    }

    private static async Task<IResult> GetTopArtistsAsync([FromQuery] int? limit, ClaimsPrincipal user, IMusicManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var artists = await manager.GetTopPlayedArtistsAsync(profileId.Value, BuildFilter(user), limit ?? 12);
        return Results.Ok(artists);
    }

    private static async Task<IResult> GetRecentlyAddedAlbumsAsync([FromQuery] int? limit, ClaimsPrincipal user, IMusicManager manager)
    {
        var albums = await manager.GetRecentlyAddedAlbumsAsync(BuildFilter(user), limit ?? 12);
        return Results.Ok(albums);
    }

    private static async Task<IResult> GetMixesAsync(ClaimsPrincipal user, IMusicRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var mixes = await manager.GetMixesForProfileAsync(profileId.Value, BuildFilter(user));
        return Results.Ok(mixes);
    }

    private static async Task<IResult> GetMixDetailAsync(Guid mixId, ClaimsPrincipal user, IMusicRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var detail = await manager.GetMixDetailAsync(mixId, profileId.Value, BuildFilter(user));
        if (detail == null) return Results.NotFound();
        return Results.Ok(detail);
    }

    private static async Task<IResult> GetBecauseYouPlayedAsync(ClaimsPrincipal user, IMusicRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var rows = await manager.GetBecauseYouPlayedRowsAsync(profileId.Value, BuildFilter(user));
        return Results.Ok(rows);
    }

    private static IResult RefreshRecommendationsAsync(ClaimsPrincipal user, ITaskQueueManager taskQueue)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();

        if (user.IsAdmin())
        {
            taskQueue.EnqueueTask("Refresh Music Recommendations (All Profiles)", async (ct, sp) =>
            {
                var manager = sp.GetRequiredService<IMusicRecommendationManager>();
                await manager.RefreshAllActiveProfilesAsync(ct);
            });
            return Results.Accepted();
        }

        var pid = profileId.Value;
        taskQueue.EnqueueTask($"Refresh Music Recommendations: {pid}", async (ct, sp) =>
        {
            var manager = sp.GetRequiredService<IMusicRecommendationManager>();
            await manager.RefreshMixesForProfileAsync(pid);
        });
        return Results.Accepted();
    }

    private static async Task<IResult> StartRadioAsync([FromBody] StartRadioRequest request, ClaimsPrincipal user, IMusicRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var seed = ParseSeed(request);
        if (seed == null) return Results.BadRequest(new { error = "Invalid radio seed." });
        var queue = await manager.StartRadioAsync(profileId.Value, BuildFilter(user), seed, request.Size ?? 50);
        return Results.Ok(queue);
    }

    private static async Task<IResult> ExtendRadioAsync([FromBody] ExtendRadioRequest request, ClaimsPrincipal user, IMusicRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var seed = ParseSeed(request);
        if (seed == null) return Results.BadRequest(new { error = "Invalid radio seed." });
        var excludeIds = request.ExcludeTrackIds ?? new List<Guid>();
        var queue = await manager.ExtendRadioAsync(profileId.Value, BuildFilter(user), seed, excludeIds, request.Size ?? 25);
        return Results.Ok(queue);
    }

    private static async Task<IResult> GetStationsAsync(ClaimsPrincipal user, IMusicRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var stations = await manager.GetStationsForProfileAsync(profileId.Value);
        return Results.Ok(stations);
    }

    private static async Task<IResult> CreateStationAsync([FromBody] CreateStationRequest request, ClaimsPrincipal user, IMusicRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var seed = ParseSeed(request);
        if (seed == null) return Results.BadRequest(new { error = "Invalid station seed." });
        var station = await manager.SaveStationAsync(profileId.Value, BuildFilter(user), request.Name ?? string.Empty, seed);
        if (station == null) return Results.BadRequest(new { error = "Could not save station." });
        return Results.Ok(station);
    }

    private static async Task<IResult> DeleteStationAsync(Guid stationId, ClaimsPrincipal user, IMusicRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        await manager.DeleteStationAsync(profileId.Value, stationId);
        return Results.NoContent();
    }

    private static async Task<IResult> TouchStationAsync(Guid stationId, ClaimsPrincipal user, IMusicRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        await manager.TouchStationLastPlayedAsync(profileId.Value, stationId);
        return Results.NoContent();
    }

    private static async Task<IResult> GetYearRecapAsync([FromQuery] int? year, ClaimsPrincipal user, IMusicRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var targetYear = year ?? DateTime.UtcNow.Year;
        var recap = await manager.GetYearRecapAsync(profileId.Value, BuildFilter(user), targetYear);
        return Results.Ok(recap);
    }

    private static async Task<IResult> GetYearsWithHistoryAsync(ClaimsPrincipal user, IMusicRecommendationManager manager)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var years = await manager.GetYearsWithHistoryAsync(profileId.Value);
        return Results.Ok(years);
    }

    private static async Task<IResult> GetSimilarArtistsAsync(Guid artistId, ClaimsPrincipal user, IMusicRecommendationManager manager, CancellationToken cancellationToken)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        var artists = await manager.GetSimilarArtistsAsync(artistId, BuildFilter(user), cancellationToken);
        return Results.Ok(artists);
    }

    private static async Task<IResult> GetGenresAsync(ClaimsPrincipal user, IMusicManager manager)
    {
        var genres = await manager.GetGenresAsync(BuildFilter(user));
        return Results.Ok(genres);
    }

    private static async Task<IResult> GetGenreContentAsync(string genre, ClaimsPrincipal user, IMusicManager manager)
    {
        var content = await manager.GetGenreContentAsync(Uri.UnescapeDataString(genre), BuildFilter(user));
        if (content == null) return Results.NotFound();
        return Results.Ok(content);
    }

    private static async Task<IResult> HeartbeatAsync([FromBody] PlaybackHeartbeatRequest request, ClaimsPrincipal user, IServerPlaybackTracker tracker, Vora.Application.Users.IUserRepository userRepo, IClientNotifier notifier)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        if (request.TrackId == Guid.Empty || string.IsNullOrWhiteSpace(request.TrackTitle)) return Results.BadRequest();

        var profile = await userRepo.GetProfileByIdAsync(profileId.Value);
        if (profile == null) return Results.Forbid();

        tracker.Heartbeat(new ServerPlaybackHeartbeat
        {
            ProfileId = profileId.Value,
            ProfileName = profile.Name,
            ProfileImageUrl = profile.ProfileImageUrl,
            TrackId = request.TrackId,
            TrackTitle = request.TrackTitle,
            Artist = request.Artist,
            AlbumTitle = request.AlbumTitle,
            AlbumArtworkUrl = request.AlbumArtworkUrl,
            DurationSeconds = request.DurationSeconds,
            CurrentTimeSeconds = request.CurrentTimeSeconds
        });

        _ = notifier.NotifyServerPlaybackUpdatedAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> StopPlaybackAsync(ClaimsPrincipal user, IServerPlaybackTracker tracker, IClientNotifier notifier)
    {
        var profileId = user.GetProfileId();
        if (profileId == null) return Results.Forbid();
        tracker.Stop(profileId.Value);
        _ = notifier.NotifyServerPlaybackUpdatedAsync();
        await Task.CompletedTask;
        return Results.NoContent();
    }

    private static IResult GetActivePlaybackAsync(ClaimsPrincipal user, IServerPlaybackTracker tracker)
    {
        var profileId = user.GetProfileId();
        var active = tracker.GetActive(excludeProfileId: profileId);
        return Results.Ok(active);
    }

    public sealed class PlaybackHeartbeatRequest
    {
        public Guid TrackId { get; set; }
        public string TrackTitle { get; set; } = string.Empty;
        public string? Artist { get; set; }
        public string? AlbumTitle { get; set; }
        public string? AlbumArtworkUrl { get; set; }
        public int? DurationSeconds { get; set; }
        public double? CurrentTimeSeconds { get; set; }
    }

    private static RadioSeed? ParseSeed(IRadioSeedPayload request)
    {
        if (!Enum.TryParse<StationSeedKind>(request.SeedKind, true, out var kind)) return null;
        switch (kind)
        {
            case StationSeedKind.Artist:
                if (!request.SeedArtistId.HasValue) return null;
                return new RadioSeed { Kind = kind, ArtistId = request.SeedArtistId };
            case StationSeedKind.Track:
                if (!request.SeedTrackId.HasValue) return null;
                return new RadioSeed { Kind = kind, TrackId = request.SeedTrackId };
            case StationSeedKind.Genre:
                if (string.IsNullOrWhiteSpace(request.SeedGenre)) return null;
                return new RadioSeed { Kind = kind, Genre = request.SeedGenre };
        }
        return null;
    }

    public interface IRadioSeedPayload
    {
        string SeedKind { get; }
        Guid? SeedArtistId { get; }
        Guid? SeedTrackId { get; }
        string? SeedGenre { get; }
    }

    public sealed class StartRadioRequest : IRadioSeedPayload
    {
        public string SeedKind { get; set; } = string.Empty;
        public Guid? SeedArtistId { get; set; }
        public Guid? SeedTrackId { get; set; }
        public string? SeedGenre { get; set; }
        public int? Size { get; set; }
    }

    public sealed class ExtendRadioRequest : IRadioSeedPayload
    {
        public string SeedKind { get; set; } = string.Empty;
        public Guid? SeedArtistId { get; set; }
        public Guid? SeedTrackId { get; set; }
        public string? SeedGenre { get; set; }
        public List<Guid>? ExcludeTrackIds { get; set; }
        public int? Size { get; set; }
    }

    public sealed class CreateStationRequest : IRadioSeedPayload
    {
        public string? Name { get; set; }
        public string SeedKind { get; set; } = string.Empty;
        public Guid? SeedArtistId { get; set; }
        public Guid? SeedTrackId { get; set; }
        public string? SeedGenre { get; set; }
    }

    private static async Task<IResult> StreamTrackAsync(Guid trackId, [FromQuery] string? quality, IMusicManager manager, IAudioTranscodeService audioTranscodeService, HttpContext httpContext)
    {
        var path = await manager.GetTrackFilePathAsync(trackId, MusicAccessFilter.Unrestricted);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Results.NotFound();

        var bitrate = ResolveAudioBitrate(quality);
        if (bitrate <= 0)
        {
            var directContentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp3" => "audio/mpeg",
                ".flac" => "audio/flac",
                ".m4a" => "audio/mp4",
                ".aac" => "audio/aac",
                ".ogg" => "audio/ogg",
                ".opus" => "audio/opus",
                ".wav" => "audio/wav",
                ".wma" => "audio/x-ms-wma",
                _ => "application/octet-stream"
            };
            return Results.File(path, directContentType, enableRangeProcessing: true);
        }

        const string targetCodec = "mp3";
        var contentType = audioTranscodeService.ResolveContentType(targetCodec);
        var ct = httpContext.RequestAborted;
        return Results.Stream(output => audioTranscodeService.WriteTranscodedAudioAsync(path, bitrate, targetCodec, output, ct), contentType: contentType);
    }

    private static int ResolveAudioBitrate(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality)) return 0;
        return quality.ToLowerInvariant() switch
        {
            "high" => 320,
            "medium" => 192,
            "low" => 128,
            _ => 0
        };
    }
}
