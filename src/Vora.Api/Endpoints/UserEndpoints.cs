using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;

namespace Vora.Api.Endpoints;

public class UpdateUserDto
{
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? NewPassword { get; set; }
    public bool? EmailNotifyOnRequestAvailable { get; set; }
}

public class UpdateUserAccessDto
{
    public bool HasAllLibraryAccess { get; set; }
    public List<Guid> AllowedLibraryIds { get; set; } = new();
    public bool CanRequestMedia { get; set; }
    public bool AutoApproveRequests { get; set; }
    public bool EnableAiRecommendations { get; set; }
    public bool HasAllIptvAccess { get; set; }
    public List<Guid> AllowedIptvPlaylistIds { get; set; } = new();
    public bool CanRecordLiveTv { get; set; }
    public long DvrStorageQuotaBytes { get; set; }
    public bool CanTimeshiftIptv { get; set; }
    public bool CanAddCustomPodcastFeeds { get; set; }
}

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        MapClientEndpoints(routes);
        MapAdminEndpoints(routes);
        return routes;
    }

    private static void MapClientEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/{userId:guid}", GetUserAsync)
            .WithName("GetUserAccount")
            .Produces<UserVM>(StatusCodes.Status200OK);

        group.MapGet("/{userId:guid}/play-history", GetPlayHistoryAsync);

        group.MapPut("/{userId:guid}", UpdateUserAsync)
            .WithName("UpdateUserAccount")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static void MapAdminEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users").WithTags("Users (Admin)").RequireAuthorization("AdminOnly");

        group.MapGet("/", GetAllUsersAsync)
            .Produces<List<UserVM>>(StatusCodes.Status200OK);

        group.MapPut("/{userId:guid}/access", UpdateUserAccessAsync);
    }

    private static async Task<IResult> GetAllUsersAsync(IUserManager manager)
    {
        var users = await manager.GetAllUsersAsync();
        return Results.Ok(users);
    }

    private static async Task<IResult> GetUserAsync(Guid userId, IUserManager manager)
    {
        var user = await manager.GetUserAccountAsync(userId);
        return user != null ? Results.Ok(user) : Results.NotFound();
    }

    private static async Task<IResult> GetPlayHistoryAsync(
        Guid userId,
        [FromQuery] Guid? profileId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] string? search,
        [FromQuery] string? typeFilter,
        IUserManager manager)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 25;

        var result = await manager.GetUserPlayHistoryAsync(userId, profileId, page, pageSize, search ?? string.Empty, typeFilter ?? "All");
        return Results.Ok(new { result.Data, result.Total });
    }

    private static async Task<IResult> UpdateUserAsync(Guid userId, [FromBody] UpdateUserDto request, ClaimsPrincipal user, IUserManager manager)
    {
        var callingAccountId = user.GetAccountId();
        if (callingAccountId is null)
        {
            return Results.Forbid();
        }

        var callerIsAdmin = user.IsAdmin();
        if (!callerIsAdmin && callingAccountId.Value != userId)
        {
            return Results.Forbid();
        }

        try
        {
            await manager.UpdateUserAccountAsync(userId, callingAccountId.Value, callerIsAdmin, request.Email, request.DisplayName, request.NewPassword, request.EmailNotifyOnRequestAvailable);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateUserAccessAsync(Guid userId, [FromBody] UpdateUserAccessDto request, IUserManager manager)
    {
        await manager.UpdateUserAccessAsync(
            userId,
            request.HasAllLibraryAccess,
            request.AllowedLibraryIds,
            request.CanRequestMedia,
            request.AutoApproveRequests,
            request.EnableAiRecommendations,
            request.HasAllIptvAccess,
            request.AllowedIptvPlaylistIds,
            request.CanRecordLiveTv,
            request.DvrStorageQuotaBytes,
            request.CanTimeshiftIptv,
            request.CanAddCustomPodcastFeeds);
        return Results.NoContent();
    }
}
