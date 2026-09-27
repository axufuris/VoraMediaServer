using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Iptv;
using Vora.Application.Iptv.ViewModels;

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
        var group = routes.MapGroup("/api/iptv/dvr").WithTags("DVR").RequireAuthorization().RequireFeature(FeatureGate.Dvr)
            .AddEndpointFilter<AccountOwnershipFilter>();

        group.MapGet("/sessions/{profileId:guid}", GetSessionsAsync)
            .WithName("GetDvrSessions")
            .Produces<IEnumerable<IptvRecordingSessionVM>>(StatusCodes.Status200OK);
        group.MapPost("/schedule", ScheduleRecordingAsync)
            .WithName("ScheduleDvrRecording")
            .Produces<ScheduleRecordingResponse>(StatusCodes.Status200OK);
        group.MapDelete("/sessions/{sessionId:guid}", DeleteSessionAsync)
            .WithName("DeleteDvrSession");
        group.MapDelete("/series/{sessionId:guid}", CancelSeriesAsync)
            .WithName("CancelDvrSeries");

        return group;
    }

    private static async Task<IResult> GetSessionsAsync(Guid profileId, IIptvRepository repo)
    {
        var sessions = await repo.GetSessionsForProfileAsync(profileId);

        var viewModels = sessions.Select(s => new IptvRecordingSessionVM
        {
            Id = s.Id,
            Title = s.Title,
            EpisodeTitle = s.EpisodeTitle,
            SeasonNumber = s.SeasonNumber,
            EpisodeNumber = s.EpisodeNumber,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            Status = s.Status.ToString(),
            OutputFilePath = s.OutputFilePath,
            ErrorMessage = s.ErrorMessage,
            CommercialMarkersJson = s.CommercialMarkersJson,
            FileSizeBytes = s.FileSizeBytes,
            ExternalProgramId = s.ExternalProgramId,
            Schedule = new IptvRecordingScheduleVM
            {
                IsSeries = s.Schedule?.IsSeriesRecording ?? false,
                Channel = new IptvRecordingChannelVM
                {
                    Name = s.Schedule?.Channel?.Name ?? "Unknown Channel",
                    LogoUrl = s.Schedule?.Channel?.LogoUrl,
                },
            },
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

            return Results.Ok(new ScheduleRecordingResponse
            {
                Id = schedule.Id,
                Title = schedule.Title,
                ChannelId = schedule.ChannelId,
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
    }

    private static async Task<IResult> DeleteSessionAsync(Guid sessionId, ClaimsPrincipal user, IIptvRepository repo, IDvrManager dvrManager)
    {
        if (!await CallerOwnsSessionAsync(user, sessionId, repo)) return Results.Forbid();
        await dvrManager.DeleteRecordingAsync(sessionId);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelSeriesAsync(Guid sessionId, ClaimsPrincipal user, IIptvRepository repo, IDvrManager dvrManager)
    {
        if (!await CallerOwnsSessionAsync(user, sessionId, repo)) return Results.Forbid();
        await dvrManager.CancelSeriesAsync(sessionId);
        return Results.NoContent();
    }

    private static async Task<bool> CallerOwnsSessionAsync(ClaimsPrincipal user, Guid sessionId, IIptvRepository repo)
    {
        if (user.IsAdmin()) return true;
        var accountId = user.GetAccountId();
        if (accountId == null) return false;
        var session = await repo.GetSessionByIdAsync(sessionId);
        return session?.Schedule?.UserId == accountId.Value;
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
