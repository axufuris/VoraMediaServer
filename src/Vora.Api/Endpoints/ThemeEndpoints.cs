using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Vora.Application.Themes;

namespace Vora.Api.Endpoints;

public static class ThemeEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public static RouteGroupBuilder MapThemeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/themes").WithTags("Admin Themes");

        // Active theme id is profile-accessible because the ThemeProvider needs
        // it before the admin pages even render — any signed-in profile may
        // read which theme to apply. The list of all themes and the setter
        // remain admin-only.
        group.MapGet("/active", GetActiveAsync)
            .RequireAuthorization()
            .Produces<ActiveThemeResponse>();

        group.MapGet("/", GetAllAsync)
            .RequireAuthorization("AdminOnly")
            .Produces<List<ThemeMetaVM>>();

        group.MapPut("/active", SetActiveAsync)
            .RequireAuthorization("AdminOnly")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        // Hot-reload theme bundles from disk. Use case: an admin drops a new
        // bundle into <install>/Themes/<id>/ and wants it picked up without
        // restarting the API. Returns the new total bundle count.
        group.MapPost("/rescan", RescanAsync)
            .RequireAuthorization("AdminOnly")
            .Produces<RescanResponse>();

        // Full manifest for a plugin theme. Built-in themes 404 here because
        // the frontend bundles their manifests at build time. Auth required
        // (same reasoning as /active — the ThemeProvider needs this on boot
        // when the active theme is a plugin one).
        group.MapGet("/{themeId}/manifest", GetManifestAsync)
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // Asset bytes from inside a plugin theme bundle. Deliberately
        // unauthenticated: these URLs are consumed by CSS `background-image:
        // url(...)` and `<img src>`, neither of which sends our JWT bearer
        // header. The asset service enforces path-traversal protection, and
        // the bytes themselves are non-sensitive (background images, preview
        // thumbnails). Theme metadata and the active-theme setter remain
        // auth-gated; only the raw assets are public.
        group.MapGet("/{themeId}/assets/{*assetPath}", GetAssetAsync)
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> GetAllAsync(IThemeManager manager)
    {
        var list = await manager.GetAllAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> GetActiveAsync(IThemeManager manager)
    {
        var id = await manager.GetActiveIdAsync();
        return Results.Ok(new ActiveThemeResponse(id));
    }

    private static async Task<IResult> SetActiveAsync([FromBody] SetActiveThemeRequest request, IThemeManager manager)
    {
        if (string.IsNullOrWhiteSpace(request.ThemeId))
        {
            return Results.BadRequest(new { message = "themeId is required." });
        }

        var ok = await manager.SetActiveIdAsync(request.ThemeId);
        return ok ? Results.NoContent() : Results.NotFound(new { message = $"Unknown themeId: {request.ThemeId}" });
    }

    private static IResult GetManifestAsync(string themeId, IThemeRegistry registry)
    {
        var json = registry.GetManifestJson(themeId);
        if (json == null)
        {
            // Either the theme doesn't exist, or it's a built-in (frontend has it locally).
            return Results.NotFound(new { message = $"No server-side manifest for theme: {themeId}" });
        }
        return Results.Content(json, contentType: "application/json");
    }

    private static IResult GetAssetAsync(string themeId, string assetPath, IThemeAssetService assetService)
    {
        var absolutePath = assetService.ResolveAssetPath(themeId, assetPath);
        if (absolutePath == null)
        {
            return Results.NotFound();
        }

        if (!ContentTypeProvider.TryGetContentType(absolutePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        // Cache aggressively — theme assets change only when the bundle is
        // updated, which requires a server restart anyway.
        return Results.File(absolutePath, contentType: contentType, enableRangeProcessing: true);
    }

    private static IResult RescanAsync(IThemeManager manager)
    {
        var count = manager.RescanBundles();
        return Results.Ok(new RescanResponse(count));
    }

    public record ActiveThemeResponse(string ThemeId);
    public record SetActiveThemeRequest(string ThemeId);
    public record RescanResponse(int BundleCount);
}
