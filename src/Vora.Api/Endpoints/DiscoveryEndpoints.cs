using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Discovery;
using Vora.Application.Discovery.Requests;
using Vora.Application.Discovery.ViewModels;
using Vora.Application.Requests;
using Vora.Application.Users;
using Vora.Plugins.Dtos;

namespace Vora.Api.Endpoints;

public static class DiscoveryEndpoints
{
    public static RouteGroupBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/discovery").WithTags("Discovery").RequireAuthorization().RequireFeature(FeatureGate.Discover);

        group.MapGet("/config", GetAdminConfigsAsync)
            .WithName("ListDiscoveryConfigs")
            .Produces<List<DiscoveryRowConfigVM>>(StatusCodes.Status200OK);
        group.MapPut("/config", UpdateAdminConfigsAsync).RequireAuthorization("AdminOnly");

        group.MapGet("/rows/{providerId}/{rowId}/items", GetRowItemsAsync)
            .WithName("ListDiscoveryRowItems")
            .Produces<IEnumerable<DiscoveryItemVM>>(StatusCodes.Status200OK);
        group.MapGet("/details/{providerId}/{type}/{externalId}", GetItemDetailsAsync)
            .WithName("GetDiscoveryItemDetails")
            .Produces<DiscoveryItemDetailsVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/actor/{providerId}/{externalId}", GetActorAsync)
            .WithName("GetDiscoveryActor")
            .Produces<Vora.Application.Discovery.ViewModels.DiscoveryActorVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/search", SearchAsync)
            .WithName("SearchDiscovery")
            .Produces<IEnumerable<DiscoveryItemVM>>(StatusCodes.Status200OK);

        group.MapGet("/theater/showtimes", GetShowtimesAsync)
            .WithName("ListMovieShowtimes")
            .Produces<IEnumerable<TheaterDto>>(StatusCodes.Status200OK);
        group.MapGet("/theater/auto-load", IsTheaterAutoLoadEnabledAsync)
            .WithName("GetTheaterAutoLoad")
            .Produces<bool>(StatusCodes.Status200OK);

        return group;
    }

    private static async Task<IResult> GetAdminConfigsAsync(IDiscoveryManager manager)
    {
        var configs = await manager.GetAdminRowConfigsAsync();
        var vms = configs.Select(c => new DiscoveryRowConfigVM
        {
            Id = c.Id,
            RowId = c.RowId,
            ProviderId = c.ProviderId,
            Name = c.Name,
            OrderIndex = c.OrderIndex,
            IsEnabled = c.IsEnabled,
            ProviderName = c.ProviderName,
        }).ToList();
        return Results.Ok(vms);
    }

    private static async Task<IResult> UpdateAdminConfigsAsync([FromBody] List<DiscoveryRowConfigRequest> configs, IDiscoveryManager manager)
    {
        await manager.UpdateAdminRowConfigsAsync(configs);
        return Results.NoContent();
    }

    private static async Task<IResult> GetRowItemsAsync(string providerId, string rowId, [FromQuery] int page, IDiscoveryManager manager, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Results.Ok(await manager.GetRowItemsAsync(providerId, rowId, page > 0 ? page : 1, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Discovery provider error");
        }
    }

    private static async Task<IResult> GetItemDetailsAsync(string providerId, string type, string externalId, IDiscoveryManager manager, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var details = await manager.GetItemDetailsAsync(providerId, externalId, type, cancellationToken);
        return details != null ? Results.Ok(details) : Results.NotFound();
    }

    private static async Task<IResult> GetActorAsync(string providerId, string externalId, IDiscoveryManager manager, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = await manager.GetActorDetailsAsync(providerId, externalId, cancellationToken);
        return actor != null ? Results.Ok(actor) : Results.NotFound();
    }

    private static async Task<IResult> SearchAsync([FromQuery] string q, IDiscoveryManager manager, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 3)
        {
            return Results.Ok(new List<DiscoveryItemVM>());
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Results.Ok(await manager.SearchAsync(q, cancellationToken));
    }

    private static async Task<IResult> GetShowtimesAsync(
        [FromQuery] string movieTitle,
        [FromQuery] string? location,
        [FromQuery] int? maxTheaters,
        HttpContext ctx,
        IDiscoveryManager manager,
        IUserManager userManager)
    {
        if (string.IsNullOrWhiteSpace(movieTitle))
        {
            return Results.BadRequest("Movie title is required");
        }

        var effectiveLocation = location;
        if (string.IsNullOrWhiteSpace(effectiveLocation))
        {
            var profileId = ctx.User.GetProfileId();
            if (profileId.HasValue)
            {
                effectiveLocation = await userManager.GetShowtimesLocationAsync(profileId.Value);
            }
        }

        var showtimes = await manager.GetShowtimesAsync(movieTitle, effectiveLocation ?? string.Empty, DateTime.UtcNow, maxTheaters);
        return Results.Ok(showtimes);
    }

    private static async Task<IResult> IsTheaterAutoLoadEnabledAsync(IDiscoveryManager manager)
    {
        var autoLoad = await manager.IsTheaterAutoLoadEnabledAsync();
        return Results.Ok(autoLoad);
    }
}
