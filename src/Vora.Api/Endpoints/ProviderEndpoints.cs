using Microsoft.AspNetCore.Mvc;
using Vora.Application.Providers;
using Vora.Application.Providers.ViewModels;

namespace Vora.Api.Endpoints;

public class LinkProviderRequest
{
    public required string ProviderName { get; set; }
    public required string AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public static class ProviderEndpoints
{
    public static RouteGroupBuilder MapProviderEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users/{userId:guid}/providers").WithTags("External Providers").RequireAuthorization();

        group.MapGet("/", GetUserConnectionsAsync)
            .Produces<IEnumerable<ProviderConnectionVM>>(StatusCodes.Status200OK);

        group.MapPost("/", LinkProviderAsync)
            .Produces(StatusCodes.Status200OK);

        group.MapDelete("/{providerName}", UnlinkProviderAsync)
            .Produces(StatusCodes.Status204NoContent);

        return group;
    }

    private static async Task<IResult> GetUserConnectionsAsync(Guid userId, IProviderConnectionManager manager)
    {
        var connections = await manager.GetUserConnectionsAsync(userId);
        return Results.Ok(connections);
    }

    private static async Task<IResult> LinkProviderAsync(Guid userId, [FromBody] LinkProviderRequest request, IProviderConnectionManager manager)
    {
        await manager.LinkProviderAsync(userId, request.ProviderName, request.AccessToken, request.RefreshToken, request.ExpiresAt);
        return Results.Ok();
    }

    private static async Task<IResult> UnlinkProviderAsync(Guid userId, string providerName, IProviderConnectionManager manager)
    {
        await manager.UnlinkProviderAsync(userId, providerName);
        return Results.NoContent();
    }
}
