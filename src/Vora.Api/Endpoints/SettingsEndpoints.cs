using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;
using Vora.Application.Plugins.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Settings.ViewModels;

namespace Vora.Api.Endpoints;

public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettingsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/settings").WithTags("Settings").RequireAuthorization("AdminOnly");

        group.MapGet("/server", GetServerSettingsAsync)
            .Produces<ServerSettingsVM>(StatusCodes.Status200OK);

        group.MapPut("/server", UpdateServerSettingsAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/plugins/{pluginId}", GetPluginSettingsAsync)
            .Produces<List<PluginSettingFieldVM>>(StatusCodes.Status200OK);

        group.MapPut("/plugins/{pluginId}", UpdatePluginSettingsAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/hardware-devices", GetHardwareDevices)
            .Produces<List<string>>(StatusCodes.Status200OK);

        group.MapPut("/features", UpdateFeatureFlagsAsync)
            .Produces(StatusCodes.Status204NoContent);

        routes.MapGet("/api/server/features", GetFeatureFlagsAsync)
            .WithTags("Settings")
            .RequireAuthorization()
            .Produces<FeatureFlagsVM>(StatusCodes.Status200OK);

        return group;
    }

    private static async Task<IResult> GetServerSettingsAsync(ISystemSettingsManager manager)
    {
        var settings = await manager.GetServerSettingsAsync();
        return Results.Ok(settings);
    }

    private static async Task<IResult> UpdateServerSettingsAsync([FromBody] ServerSettingsVM request, ISystemSettingsManager manager)
    {
        await manager.UpdateServerSettingsAsync(request);
        return Results.NoContent();
    }

    private static async Task<IResult> GetPluginSettingsAsync(string pluginId, ISystemSettingsManager manager)
    {
        var settings = await manager.GetPluginSettingsAsync(pluginId);
        return Results.Ok(settings);
    }

    private static async Task<IResult> UpdatePluginSettingsAsync(string pluginId, [FromBody] Dictionary<string, string> settings, ISystemSettingsManager manager)
    {
        await manager.UpdatePluginSettingsAsync(pluginId, settings);
        return Results.NoContent();
    }

    private static async Task<IResult> GetFeatureFlagsAsync(ISystemSettingsManager manager)
    {
        var flags = await manager.GetFeatureFlagsAsync();
        return Results.Ok(flags);
    }

    private static async Task<IResult> UpdateFeatureFlagsAsync([FromBody] UpdateFeatureFlagsRequest request, ISystemSettingsManager manager)
    {
        await manager.UpdateFeatureFlagsAsync(request);
        return Results.NoContent();
    }

    private static IResult GetHardwareDevices()
    {
        var devices = new List<string> { "Auto" };

        if (Directory.Exists("/dev/dri"))
        {
            devices.AddRange(Directory.GetFiles("/dev/dri", "renderD*"));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            devices.Add("0");
            devices.Add("1");
        }

        return Results.Ok(devices);
    }
}
