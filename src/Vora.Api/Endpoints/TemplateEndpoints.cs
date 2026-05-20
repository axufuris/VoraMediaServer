using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Vora.Api.Extensions;
using Vora.Application.Templates;

namespace Vora.Api.Endpoints;

public static class TemplateEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public static void MapTemplateEndpoints(this IEndpointRouteBuilder routes)
    {
        var profileGroup = routes.MapGroup("/api/templates").WithTags("Client Templates");

        profileGroup.MapGet("/active", GetActiveAsync)
            .RequireAuthorization()
            .Produces<ActiveTemplateVM>();

        profileGroup.MapGet("/", GetAllAsync)
            .RequireAuthorization()
            .Produces<List<TemplateMetaVM>>();

        profileGroup.MapPut("/active", SetActiveAsync)
            .RequireAuthorization()
            .Produces<SetActiveTemplateResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        profileGroup.MapDelete("/active", ClearActiveAsync)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent);

        profileGroup.MapGet("/{templateId}/manifest", GetManifestAsync)
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        profileGroup.MapGet("/{templateId}/assets/{*assetPath}", GetAssetAsync)
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        var adminGroup = routes.MapGroup("/api/admin/templates").WithTags("Admin Templates");

        adminGroup.MapGet("/default", GetDefaultAsync)
            .RequireAuthorization("AdminOnly")
            .Produces<DefaultTemplateResponse>();

        adminGroup.MapPut("/default", SetDefaultAsync)
            .RequireAuthorization("AdminOnly")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        adminGroup.MapPost("/rescan", RescanAsync)
            .RequireAuthorization("AdminOnly")
            .Produces<TemplateRescanResponse>();

        adminGroup.MapGet("/schedules", GetSchedulesAsync)
            .RequireAuthorization("AdminOnly")
            .Produces<List<TemplateScheduleVM>>();

        adminGroup.MapPost("/schedules", CreateScheduleAsync)
            .RequireAuthorization("AdminOnly")
            .Produces<TemplateScheduleVM>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        adminGroup.MapPut("/schedules/{id:guid}", UpdateScheduleAsync)
            .RequireAuthorization("AdminOnly")
            .Produces<TemplateScheduleVM>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        adminGroup.MapDelete("/schedules/{id:guid}", DeleteScheduleAsync)
            .RequireAuthorization("AdminOnly")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetActiveAsync(HttpContext httpContext, IClientTemplateManager manager)
    {
        var profileId = httpContext.User.GetProfileId();
        if (profileId == null) return Results.Unauthorized();
        var active = await manager.GetActiveAsync(profileId.Value);
        return Results.Ok(active);
    }

    private static async Task<IResult> GetAllAsync(IClientTemplateManager manager)
    {
        var list = await manager.GetAllAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> SetActiveAsync(
        HttpContext httpContext,
        [FromBody] SetActiveTemplateRequest request,
        IClientTemplateManager manager)
    {
        var profileId = httpContext.User.GetProfileId();
        if (profileId == null) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.TemplateId))
        {
            return Results.BadRequest(new { message = "templateId is required." });
        }

        try
        {
            var result = await manager.SetActiveAsync(profileId.Value, request.TemplateId);
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> ClearActiveAsync(HttpContext httpContext, IClientTemplateManager manager)
    {
        var profileId = httpContext.User.GetProfileId();
        if (profileId == null) return Results.Unauthorized();
        await manager.ClearActiveAsync(profileId.Value);
        return Results.NoContent();
    }

    private static IResult GetManifestAsync(string templateId, IClientTemplateRegistry registry)
    {
        var json = registry.GetManifestJson(templateId);
        if (json == null)
        {
            return Results.NotFound(new { message = $"No server-side manifest for template: {templateId}" });
        }
        return Results.Content(json, contentType: "application/json");
    }

    private static IResult GetAssetAsync(string templateId, string assetPath, IClientTemplateAssetService assetService)
    {
        var absolutePath = assetService.ResolveAssetPath(templateId, assetPath);
        if (absolutePath == null) return Results.NotFound();

        if (!ContentTypeProvider.TryGetContentType(absolutePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return Results.File(absolutePath, contentType: contentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> GetDefaultAsync(IClientTemplateManager manager)
    {
        var id = await manager.GetDefaultAsync();
        return Results.Ok(new DefaultTemplateResponse(id));
    }

    private static async Task<IResult> SetDefaultAsync([FromBody] SetDefaultTemplateRequest request, IClientTemplateManager manager)
    {
        if (string.IsNullOrWhiteSpace(request.TemplateId))
        {
            return Results.BadRequest(new { message = "templateId is required." });
        }
        var ok = await manager.SetDefaultAsync(request.TemplateId);
        return ok ? Results.NoContent() : Results.NotFound(new { message = $"Unknown templateId: {request.TemplateId}" });
    }

    private static IResult RescanAsync(IClientTemplateManager manager)
    {
        var count = manager.RescanBundles();
        return Results.Ok(new TemplateRescanResponse(count));
    }

    private static async Task<IResult> GetSchedulesAsync(IClientTemplateScheduleManager manager)
    {
        var list = await manager.GetAllAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> CreateScheduleAsync(
        [FromBody] CreateTemplateScheduleRequest request,
        IClientTemplateScheduleManager manager)
    {
        try
        {
            var vm = await manager.CreateAsync(request);
            return Results.Created($"/api/admin/templates/schedules/{vm.Id}", vm);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> UpdateScheduleAsync(
        Guid id,
        [FromBody] UpdateTemplateScheduleRequest request,
        IClientTemplateScheduleManager manager)
    {
        try
        {
            var vm = await manager.UpdateAsync(id, request);
            return vm == null ? Results.NotFound() : Results.Ok(vm);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> DeleteScheduleAsync(Guid id, IClientTemplateScheduleManager manager)
    {
        var ok = await manager.DeleteAsync(id);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    public record DefaultTemplateResponse(string TemplateId);
}
