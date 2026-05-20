using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Iptv;

namespace Vora.Api.Endpoints;

public class ScheduleRecordingDto
{
    public Guid ProfileId { get; set; }
    public Guid ChannelId { get; set; }
    public required string Title { get; set; }
    public string? ProgramId { get; set; }
    public bool IsSeries { get; set; }
    public int KeepMaxEpisodes { get; set; }
}

public static class DvrEndpoints
{
    public static RouteGroupBuilder MapDvrEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/iptv/dvr").WithTags("DVR").RequireAuthorization().RequireFeature(FeatureGate.Dvr);

        group.MapGet("/sessions/{profileId:guid}", GetSessionsAsync);
        group.MapPost("/schedule", ScheduleRecordingAsync);
        group.MapDelete("/sessions/{sessionId:guid}", DeleteSessionAsync);
        group.MapDelete("/series/{sessionId:guid}", CancelSeriesAsync);

        return group;
    }

    private static async Task<IResult> GetSessionsAsync(Guid profileId, IIptvRepository repo)
    {
        var sessions = await repo.GetSessionsForProfileAsync(profileId);

        var viewModels = sessions.Select(s => new
        {
            id = s.Id,
            title = s.Title,
            episodeTitle = s.EpisodeTitle,
            seasonNumber = s.SeasonNumber,
            episodeNumber = s.EpisodeNumber,
            startTime = s.StartTime,
            endTime = s.EndTime,
            status = s.Status,
            outputFilePath = s.OutputFilePath,
            errorMessage = s.ErrorMessage,
            commercialMarkersJson = s.CommercialMarkersJson,
            fileSizeBytes = s.FileSizeBytes,
            externalProgramId = s.ExternalProgramId,
            schedule = new
            {
                isSeries = s.Schedule?.IsSeriesRecording ?? false,
                channel = new
                {
                    name = s.Schedule?.Channel?.Name ?? "Unknown Channel",
                    logoUrl = s.Schedule?.Channel?.LogoUrl
                }
            }
        });

        return Results.Ok(viewModels);
    }

    private static async Task<IResult> ScheduleRecordingAsync(
        [FromBody] ScheduleRecordingDto request,
        ClaimsPrincipal user,
        IDvrManager dvrManager,
        IIptvRepository repo)
    {
        var profileId = user.GetProfileId();
        if (profileId == null)
        {
            return Results.Unauthorized();
        }

        try
        {
            if (!await HasDvrQuotaCapacityAsync(profileId.Value, repo))
            {
                return Results.BadRequest("DVR storage quota exceeded. Please delete older recordings to free up space.");
            }

            var schedule = await dvrManager.ScheduleRecordingAsync(
                profileId.Value,
                request.ChannelId,
                request.Title,
                request.ProgramId,
                request.IsSeries,
                request.KeepMaxEpisodes);

            return Results.Ok(new
            {
                id = schedule.Id,
                title = schedule.Title,
                channelId = schedule.ChannelId
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
    }

    private static async Task<IResult> DeleteSessionAsync(Guid sessionId, IDvrManager dvrManager)
    {
        await dvrManager.DeleteRecordingAsync(sessionId);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelSeriesAsync(Guid sessionId, IDvrManager dvrManager)
    {
        await dvrManager.CancelSeriesAsync(sessionId);
        return Results.NoContent();
    }

    private static async Task<bool> HasDvrQuotaCapacityAsync(Guid profileId, IIptvRepository repo)
    {
        var profile = await repo.GetUserProfileAsync(profileId);
        if (profile == null)
        {
            return true;
        }

        var owner = await repo.GetUserWithQuotaAsync(profile.UserId);
        if (owner.DvrStorageQuotaBytes <= 0)
        {
            return true;
        }

        var currentUsage = await repo.GetDvrUsageBytesAsync(owner.Id);
        return currentUsage < owner.DvrStorageQuotaBytes;
    }
}
