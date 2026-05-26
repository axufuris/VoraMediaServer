using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Vora.Api.Hubs;
using Vora.Application.Analysis;
using Vora.Application.Settings;
using Vora.Application.Streaming;

namespace Vora.Api.Endpoints;

public record StreamCommandRequest(string Command, string? Message);

public static class StreamingAdminEndpoints
{
    public static RouteGroupBuilder MapStreamingAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/streaming/admin").WithTags("Streaming (Admin)").RequireAuthorization("AdminOnly");

        group.MapGet("/now-playing", GetNowPlayingAsync);
        group.MapGet("/history", GetHistoryAsync)
            .Produces(StatusCodes.Status200OK);
        group.MapGet("/system-stats", GetSystemStatsAsync);
        group.MapPost("/sessions/{sessionId:guid}/command", SendSessionCommandAsync);

        return group;
    }

    private static async Task<IResult> GetNowPlayingAsync(IStreamManager streamManager)
    {
        var sessions = await streamManager.GetNowPlayingSessionsAsync();
        return Results.Ok(sessions);
    }

    private static async Task<IResult> GetHistoryAsync(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] string? search,
        IStreamManager streamManager)
    {
        var result = await streamManager.GetGroupedHistoryAsync(page, pageSize, search ?? string.Empty);
        return Results.Ok(new { result.Data, result.Total });
    }

    private static async Task<IResult> GetSystemStatsAsync(IStreamManager streamManager, ISystemMetricRepository metricRepo)
    {
        var stats = await streamManager.GetSystemStatsAsync(metricRepo);
        return Results.Ok(stats);
    }

    private static async Task<IResult> SendSessionCommandAsync(
        Guid sessionId,
        IHubContext<VoraHub> hub,
        IStreamRepository streamRepository,
        [FromBody] StreamCommandRequest req)
    {
        var session = await streamRepository.GetSessionAsync(sessionId);
        if (session == null)
        {
            return Results.NotFound(new { Message = "Stream session not found." });
        }

        var payload = new
        {
            SessionId = sessionId.ToString(),
            req.Command,
            req.Message
        };

        var target = session.UserProfileId.HasValue
            ? hub.Clients.Group(VoraHub.ProfileGroupName(session.UserProfileId.Value))
            : hub.Clients.Group(VoraHub.UserGroupName(session.UserId));

        await target.SendAsync("StreamCommandReceived", payload);
        return Results.Ok();
    }
}
