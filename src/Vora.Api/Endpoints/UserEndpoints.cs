using Microsoft.AspNetCore.Mvc;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;

namespace Vora.Api.Endpoints;

public class UpdateUserDto
{
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? NewPassword { get; set; }
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
            .Produces<UserVM>(StatusCodes.Status200OK);

        group.MapGet("/{userId:guid}/play-history", GetPlayHistoryAsync);

        group.MapPut("/{userId:guid}", UpdateUserAsync);
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

    private static async Task<IResult> UpdateUserAsync(Guid userId, [FromBody] UpdateUserDto request, IUserManager manager)
    {
        await manager.UpdateUserAccountAsync(userId, request.Email, request.DisplayName, request.NewPassword);
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
