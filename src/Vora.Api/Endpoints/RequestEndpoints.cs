using Microsoft.AspNetCore.Mvc;
using Vora.Application.Requests;
using Vora.Application.Requests.Dtos;

namespace Vora.Api.Endpoints;

public class ProviderOptionsRequestDto
{
    public required string ProviderId { get; set; }
    public required string OptionType { get; set; }
    public required string Hostname { get; set; }
    public int Port { get; set; }
    public bool UseSsl { get; set; }
    public required string ApiKey { get; set; }
    public string UrlBase { get; set; } = string.Empty;
}

public static class RequestEndpoints
{
    public static IEndpointRouteBuilder MapRequestEndpoints(this IEndpointRouteBuilder routes)
    {
        MapClientEndpoints(routes);
        MapAdminEndpoints(routes);
        return routes;
    }

    private static void MapClientEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/requests").WithTags("Requests").RequireAuthorization();

        group.MapGet("/status", GetRequestStatusAsync);
    }

    private static void MapAdminEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/requests").WithTags("Requests (Admin)").RequireAuthorization("AdminOnly");

        group.MapGet("/", GetAllRequestsAsync);
        group.MapPut("/{id:guid}/approve", ApproveRequestAsync);
        group.MapDelete("/{id:guid}", DeleteRequestAsync);

        var serversGroup = group.MapGroup("/servers");
        serversGroup.MapGet("/", GetServersAsync);
        serversGroup.MapPost("/", AddServerAsync);
        serversGroup.MapPost("/options", GetProviderOptionsAsync);
        serversGroup.MapPut("/{id:guid}", UpdateServerAsync);
        serversGroup.MapDelete("/{id:guid}", DeleteServerAsync);
    }

    private static async Task<IResult> GetAllRequestsAsync(IRequestManager manager) =>
        Results.Ok(await manager.GetAllRequestsAsync());

    private static async Task<IResult> GetRequestStatusAsync([FromQuery] string externalId, [FromQuery] string type, IRequestManager manager)
    {
        var status = await manager.GetRequestStatusAsync(externalId, type);
        return Results.Ok(new { Status = status ?? -1 });
    }

    private static async Task<IResult> ApproveRequestAsync(Guid id, [FromQuery] int? profileId, IRequestManager manager)
    {
        var success = await manager.ApproveRequestAsync(id, null, profileId);
        return success
            ? Results.Ok()
            : Results.BadRequest(new { Message = "Failed to send request to the external server. Check your plugin settings." });
    }

    private static async Task<IResult> DeleteRequestAsync(Guid id, IRequestManager manager)
    {
        await manager.DeleteRequestAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> GetServersAsync(IRequestManager manager) =>
        Results.Ok(await manager.GetAllServersAsync());

    private static async Task<IResult> AddServerAsync([FromBody] SaveRequestServerDto dto, IRequestManager manager)
    {
        var created = await manager.AddServerAsync(dto);
        return Results.Ok(created);
    }

    private static async Task<IResult> GetProviderOptionsAsync([FromBody] ProviderOptionsRequestDto req, IRequestManager manager)
    {
        try
        {
            var options = await manager.GetProviderOptionsAsync(
                req.ProviderId,
                req.OptionType,
                req.Hostname,
                req.Port,
                req.UseSsl,
                req.UrlBase,
                req.ApiKey);
            return Results.Ok(options);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { ex.Message });
        }
    }

    private static async Task<IResult> UpdateServerAsync(Guid id, [FromBody] SaveRequestServerDto dto, IRequestManager manager)
    {
        await manager.UpdateServerAsync(id, dto);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteServerAsync(Guid id, IRequestManager manager)
    {
        await manager.DeleteServerAsync(id);
        return Results.NoContent();
    }
}
