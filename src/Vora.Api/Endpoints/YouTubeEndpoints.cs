using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.YouTube;
using Vora.Application.YouTube.Requests;

namespace Vora.Api.Endpoints;

public static class YouTubeEndpoints
{
    public static RouteGroupBuilder MapYouTubeEndpoints(this IEndpointRouteBuilder routes)
    {
        MapClientEndpoints(routes);
        return MapAdminEndpoints(routes);
    }

    private static RouteGroupBuilder MapClientEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/youtube").WithTags("YouTube").RequireAuthorization();

        group.MapGet("/feed", GetFeedAsync);
        group.MapGet("/trending", GetTrendingAsync);
        group.MapGet("/search", SearchAsync);
        group.MapGet("/channel/{channelId}", GetChannelAsync);
        group.MapGet("/channel/{channelId}/uploads", GetChannelUploadsAsync);
        group.MapGet("/channel/{channelId}/playlists", GetChannelPlaylistsAsync);
        group.MapGet("/video/{videoId}", GetVideoAsync);

        group.MapGet("/subscriptions", GetSubscriptionsAsync);
        group.MapPost("/subscriptions", SubscribeAsync);
        group.MapDelete("/subscriptions/{channelId}", UnsubscribeAsync);

        group.MapGet("/history", GetHistoryAsync);
        group.MapPost("/history", RecordHistoryAsync);
        group.MapDelete("/history", ClearHistoryAsync);

        group.MapGet("/settings", GetProfileSettingsAsync);
        group.MapPut("/settings", UpdateProfileSettingsAsync);

        return group;
    }

    private static RouteGroupBuilder MapAdminEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/youtube").WithTags("YouTube Admin").RequireAuthorization("AdminOnly");

        group.MapGet("/status", GetAdminStatusAsync);
        group.MapGet("/accounts/{accountId:guid}", GetAccountSettingsAsync);
        group.MapPut("/accounts/{accountId:guid}", UpdateAccountSettingsAsync);

        return group;
    }

    private static async Task<IResult> GetFeedAsync(HttpContext ctx, IYouTubeManager manager, CancellationToken ct)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            return Results.Ok(await manager.GetHomeFeedAsync(profileId.Value, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> GetTrendingAsync(HttpContext ctx, IYouTubeManager manager, CancellationToken ct)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            return Results.Ok(await manager.GetTrendingAsync(profileId.Value, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> SearchAsync(HttpContext ctx, [FromQuery] string q, [FromQuery] string? pageToken, IYouTubeManager manager, CancellationToken ct)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            return Results.Ok(await manager.SearchPageAsync(profileId.Value, q, pageToken, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> GetChannelAsync(string channelId, HttpContext ctx, IYouTubeManager manager, CancellationToken ct)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            var channel = await manager.GetChannelAsync(profileId.Value, channelId, ct);
            return channel is null ? Results.NotFound() : Results.Ok(channel);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> GetChannelUploadsAsync(string channelId, [FromQuery] string? pageToken, HttpContext ctx, IYouTubeManager manager, CancellationToken ct)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            return Results.Ok(await manager.GetChannelUploadsPageAsync(profileId.Value, channelId, pageToken, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> GetChannelPlaylistsAsync(string channelId, HttpContext ctx, IYouTubeManager manager, CancellationToken ct)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            return Results.Ok(await manager.GetChannelPlaylistsAsync(profileId.Value, channelId, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> GetVideoAsync(string videoId, HttpContext ctx, IYouTubeManager manager, CancellationToken ct)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            var video = await manager.GetVideoAsync(profileId.Value, videoId, ct);
            return video is null ? Results.NotFound() : Results.Ok(video);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> GetSubscriptionsAsync(HttpContext ctx, IYouTubeManager manager)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            return Results.Ok(await manager.GetSubscriptionsAsync(profileId.Value));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> SubscribeAsync(HttpContext ctx, [FromBody] SubscribeToChannelRequest request, IYouTubeManager manager, CancellationToken ct)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            var subscription = await manager.SubscribeAsync(profileId.Value, request, ct);
            return Results.Ok(subscription);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static async Task<IResult> UnsubscribeAsync(string channelId, HttpContext ctx, IYouTubeManager manager)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            await manager.UnsubscribeAsync(profileId.Value, channelId);
            return Results.NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> GetHistoryAsync(HttpContext ctx, IYouTubeManager manager)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            return Results.Ok(await manager.GetWatchHistoryAsync(profileId.Value));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> RecordHistoryAsync(HttpContext ctx, [FromBody] RecordWatchHistoryRequest request, IYouTubeManager manager)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            await manager.RecordWatchAsync(profileId.Value, request);
            return Results.NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ClearHistoryAsync(HttpContext ctx, IYouTubeManager manager)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        try
        {
            await manager.ClearWatchHistoryAsync(profileId.Value);
            return Results.NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> GetProfileSettingsAsync(HttpContext ctx, IYouTubeManager manager)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        return Results.Ok(await manager.GetProfileSettingsAsync(profileId.Value));
    }

    private static async Task<IResult> UpdateProfileSettingsAsync(HttpContext ctx, [FromBody] UpdateYouTubeProfileSettingsRequest request, IYouTubeManager manager)
    {
        var profileId = ctx.User.GetProfileId();
        if (!profileId.HasValue) return Results.Unauthorized();

        return Results.Ok(await manager.UpdateProfileSettingsAsync(profileId.Value, request));
    }

    private static async Task<IResult> GetAdminStatusAsync(IYouTubeManager manager) =>
        Results.Ok(await manager.GetAdminStatusAsync());

    private static async Task<IResult> GetAccountSettingsAsync(Guid accountId, IYouTubeManager manager) =>
        Results.Ok(await manager.GetAccountSettingsAsync(accountId));

    private static async Task<IResult> UpdateAccountSettingsAsync(Guid accountId, [FromBody] UpdateYouTubeAccountSettingsRequest request, IYouTubeManager manager) =>
        Results.Ok(await manager.UpdateAccountSettingsAsync(accountId, request));
}
