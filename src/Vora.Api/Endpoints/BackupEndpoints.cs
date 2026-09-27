using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Backups;
using Vora.Application.Backups.ViewModels;

namespace Vora.Api.Endpoints;

public static class BackupEndpoints
{
    public static RouteGroupBuilder MapBackupEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/backups")
            .WithTags("Admin", "Backups")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", ListAsync).Produces<List<BackupSummaryVM>>(StatusCodes.Status200OK);
        group.MapPost("/", CreateAsync).Produces<BackupSummaryVM>(StatusCodes.Status200OK);

        group.MapGet("/sections", ListSectionsAsync).Produces<List<AvailableSectionVM>>(StatusCodes.Status200OK);

        group.MapGet("/settings", GetSettingsAsync).Produces<BackupSettingsVM>(StatusCodes.Status200OK);
        group.MapPut("/settings", UpdateSettingsAsync).Produces<BackupSettingsVM>(StatusCodes.Status200OK);

        group.MapGet("/{fileName}/manifest", GetManifestAsync).Produces<BackupManifestVM>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        group.MapPost("/{fileName}/restore", RestoreAsync).Produces<RestoreBackupResult>(StatusCodes.Status200OK);
        group.MapGet("/{fileName}/download", DownloadAsync).Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        group.MapDelete("/{fileName}", DeleteAsync).Produces(StatusCodes.Status204NoContent);

        group.MapPost("/upload", UploadAsync)
            .DisableAntiforgery()
            .Produces<BackupSummaryVM>(StatusCodes.Status200OK);

        return group;
    }

    private static async Task<IResult> ListAsync(IBackupManager manager)
    {
        var list = await manager.ListBackupsAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> CreateAsync([FromBody] CreateBackupRequest? body, IBackupManager manager)
    {
        var reason = string.IsNullOrWhiteSpace(body?.Reason) ? "manual" : body!.Reason;
        try
        {
            var summary = await manager.CreateBackupAsync(reason);
            return Results.Ok(summary);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError, title: "Backup creation failed");
        }
    }

    private static async Task<IResult> ListSectionsAsync(IBackupManager manager)
    {
        var sections = await manager.GetAvailableSectionsAsync();
        return Results.Ok(sections);
    }

    private static async Task<IResult> GetSettingsAsync(IBackupSettingsStore store, IBackupManager manager)
    {
        var settings = await store.GetAsync();
        var dir = await manager.GetEffectiveDirectoryAsync();
        var sections = await manager.GetAvailableSectionsAsync();
        return Results.Ok(BackupSettingsMapper.ToVM(settings, dir, sections));
    }

    private static async Task<IResult> UpdateSettingsAsync([FromBody] BackupSettingsVM body, IBackupSettingsStore store, IBackupManager manager)
    {
        var existing = await store.GetAsync();
        var updated = BackupSettingsMapper.FromVM(body, existing);
        await store.SaveAsync(updated);
        var dir = await manager.GetEffectiveDirectoryAsync();
        var sections = await manager.GetAvailableSectionsAsync();
        return Results.Ok(BackupSettingsMapper.ToVM(updated, dir, sections));
    }

    private static async Task<IResult> GetManifestAsync(string fileName, IBackupManager manager)
    {
        var manifest = await manager.GetManifestAsync(fileName);
        return manifest == null ? Results.NotFound() : Results.Ok(manifest);
    }

    private static async Task<IResult> RestoreAsync(string fileName, [FromBody] RestoreBackupRequest request, IBackupManager manager, HttpContext ctx)
    {
        var adminId = ctx.User.GetAccountId();
        var result = await manager.RestoreBackupAsync(fileName, request, adminId);
        return Results.Ok(result);
    }

    private static async Task<IResult> DownloadAsync(string fileName, IBackupManager manager)
    {
        try
        {
            var stream = await manager.OpenBackupStreamAsync(fileName);
            return Results.File(stream, "application/zip", fileName);
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> DeleteAsync(string fileName, IBackupManager manager)
    {
        await manager.DeleteBackupAsync(fileName);
        return Results.NoContent();
    }

    private static async Task<IResult> UploadAsync(HttpRequest request, IBackupManager manager)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { Message = "Multipart form upload required." });
        }
        var form = await request.ReadFormAsync();
        var file = form.Files.FirstOrDefault();
        if (file == null || file.Length == 0)
        {
            return Results.BadRequest(new { Message = "No file uploaded." });
        }
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { Message = "Backup file must be a .zip" });
        }

        await using var stream = file.OpenReadStream();
        try
        {
            var summary = await manager.UploadBackupAsync(stream, file.FileName);
            return Results.Ok(summary);
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid backup file");
        }
    }
}
