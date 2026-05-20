using Microsoft.AspNetCore.Mvc;
using Vora.Application.Devices;
using Vora.Application.Devices.Dtos;

namespace Vora.Api.Endpoints;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder routes)
    {
        MapAdminDeviceEndpoints(routes);
        MapClientDeviceEndpoints(routes);
        return routes;
    }

    private static void MapAdminDeviceEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/devices").WithTags("Devices (Admin)").RequireAuthorization("AdminOnly");

        group.MapGet("/", GetAllDevicesAsync);
        group.MapPut("/{id:guid}/block", BlockDeviceAsync);
        group.MapPut("/{id:guid}/unblock", UnblockDeviceAsync);
        group.MapDelete("/{id:guid}", DeleteDeviceAsync);
    }

    private static void MapClientDeviceEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/devices").WithTags("Devices (Client)").RequireAuthorization();

        group.MapPut("/capabilities", UpdateCapabilitiesAsync);
    }

    private static async Task<IResult> GetAllDevicesAsync(IDeviceManager manager)
    {
        var devices = await manager.GetAllDevicesAsync();
        return Results.Ok(devices);
    }

    private static async Task<IResult> BlockDeviceAsync(Guid id, IDeviceManager manager)
    {
        var success = await manager.BlockDeviceAsync(id);
        return success ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> UnblockDeviceAsync(Guid id, IDeviceManager manager)
    {
        var success = await manager.UnblockDeviceAsync(id);
        return success ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> DeleteDeviceAsync(Guid id, IDeviceManager manager)
    {
        await manager.DeleteDeviceAsync(id);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateCapabilitiesAsync(HttpContext context, [FromBody] DeviceCapabilitiesDto dto, IDeviceManager manager)
    {
        var deviceId = context.Request.Headers["X-Vora-Device-Id"].FirstOrDefault();
        if (string.IsNullOrEmpty(deviceId))
        {
            return Results.BadRequest("DeviceId header missing.");
        }

        await manager.UpdateDeviceCapabilitiesAsync(deviceId, dto);
        return Results.NoContent();
    }
}
