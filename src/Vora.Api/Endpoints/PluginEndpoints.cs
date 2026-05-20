using Microsoft.AspNetCore.Mvc;
using Vora.Application.Plugins;
using Vora.Application.Plugins.ViewModels;

namespace Vora.Api.Endpoints;

public static class PluginEndpoints
{
    public static RouteGroupBuilder MapPluginEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/plugins").WithTags("Plugins").RequireAuthorization("AdminOnly");

        group.MapGet("/", GetActivePluginsAsync)
            .Produces<IEnumerable<PluginVM>>(StatusCodes.Status200OK);

        group.MapGet("/options", GetPluginOptionsAsync)
            .Produces<IEnumerable<PluginOptionVM>>(StatusCodes.Status200OK);

        group.MapPost("/upload", UploadPluginAsync)
            .DisableAntiforgery()
            .Produces(StatusCodes.Status200OK);

        group.MapDelete("/{id}", UninstallPluginAsync)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> GetActivePluginsAsync(IPluginManager manager)
    {
        var plugins = await manager.GetActivePluginsAsync();
        return Results.Ok(plugins);
    }

    private static async Task<IResult> GetPluginOptionsAsync([FromQuery] string type, IPluginManager manager) =>
        Results.Ok(await manager.GetPluginOptionsAsync(type));

    private static async Task<IResult> UploadPluginAsync(IFormFile file, IPluginManager manager)
    {
        try
        {
            await manager.UploadPluginAsync(file);
            return Results.Ok(new { Message = "Plugin uploaded successfully. Restart the server to load it." });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static IResult UninstallPluginAsync(string id, IPluginManager manager)
    {
        try
        {
            var success = manager.UninstallPlugin(id);
            return success
                ? Results.Ok(new { Message = "Plugin uninstalled. Restart the server to apply changes." })
                : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }
}
