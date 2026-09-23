using Microsoft.AspNetCore.Mvc;
using Vora.Application.Settings;
using Vora.Application.Settings.ViewModels;

namespace Vora.Api.Endpoints;

public static class RemoteAccessEndpoints
{
    public static RouteGroupBuilder MapRemoteAccessEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/remote-access").WithTags("Remote Access").RequireAuthorization("AdminOnly");

        group.MapGet("/", GetStatusAsync)
            .Produces<RemoteAccessStatusVM>(StatusCodes.Status200OK);
        group.MapPut("/", ApplySettingsAsync)
            .Produces<RemoteAccessStatusVM>(StatusCodes.Status200OK);

        return group;
    }

    private static async Task<IResult> GetStatusAsync(IRemoteAccessManager manager)
    {
        var status = await manager.GetStatusAsync();
        return Results.Ok(status);
    }

    private static async Task<IResult> ApplySettingsAsync([FromBody] UpdateRemoteAccessRequest request, IRemoteAccessManager manager)
    {
        var status = await manager.ApplySettingsAsync(request);
        return Results.Ok(status);
    }
}
