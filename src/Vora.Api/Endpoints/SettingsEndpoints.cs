using Microsoft.AspNetCore.Mvc;
using Vora.Application.Plugins;
using Vora.Application.Plugins.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Settings.ViewModels;
using Vora.Plugins.Dtos;

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

        group.MapPut("/registration-mode", UpdateRegistrationModeAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/plugins/{pluginId}", GetPluginSettingsAsync)
            .Produces<List<PluginSettingFieldVM>>(StatusCodes.Status200OK);

        group.MapPut("/plugins/{pluginId}", UpdatePluginSettingsAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/plugins/{pluginId}/test", TestPluginConnectionAsync)
            .Produces<PluginConnectionTestResult>(StatusCodes.Status200OK);

        group.MapGet("/hardware-devices", GetHardwareDevices)
            .Produces<List<string>>(StatusCodes.Status200OK);

        group.MapPut("/features", UpdateFeatureFlagsAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/setup-guide", GetSetupGuideAsync)
            .WithName("GetSetupGuide")
            .Produces<SetupGuideVM>(StatusCodes.Status200OK);
        group.MapPut("/setup-guide", UpdateSetupGuideAsync)
            .WithName("UpdateSetupGuide")
            .Produces<SetupGuideVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

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

    private static async Task<IResult> UpdateRegistrationModeAsync([FromBody] UpdateRegistrationModeRequest request, ISystemSettingsManager manager)
    {
        if (!Enum.IsDefined(request.Mode))
        {
            return Results.Problem("Unknown registration mode.", statusCode: StatusCodes.Status400BadRequest);
        }
        await manager.UpdateRegistrationModeAsync(request.Mode);
        return Results.NoContent();
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

    private static async Task<IResult> TestPluginConnectionAsync(string pluginId, [FromBody] Dictionary<string, string> settings, IPluginManager pluginManager)
    {
        var result = await pluginManager.TestPluginConnectionAsync(pluginId, settings);
        return Results.Ok(result);
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

    private static async Task<IResult> GetSetupGuideAsync(ISystemSettingsManager manager) =>
        Results.Ok(await manager.GetSetupGuideAsync());

    private static async Task<IResult> UpdateSetupGuideAsync([FromBody] SetupGuideVM request, ISystemSettingsManager manager)
    {
        if (!Enum.IsDefined(request.Status))
        {
            return Results.Problem("Unknown setup guide status.", statusCode: StatusCodes.Status400BadRequest);
        }
        return Results.Ok(await manager.UpdateSetupGuideAsync(request));
    }

    private static IResult GetHardwareDevices(IHardwareCapabilityService hardware)
    {
        return Results.Ok(hardware.GetAvailableTranscodingDevices());
    }
}
