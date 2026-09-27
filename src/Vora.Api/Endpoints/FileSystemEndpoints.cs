using Microsoft.AspNetCore.Mvc;
using Vora.Application.FileSystem;
using Vora.Application.FileSystem.ViewModels;

namespace Vora.Api.Endpoints;

public static class FileSystemEndpoints
{
    public static RouteGroupBuilder MapFileSystemEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/filesystem")
            .WithTags("FileSystem")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/roots", GetRootsAsync)
            .Produces<List<FileSystemRootVM>>(StatusCodes.Status200OK);

        group.MapGet("/list", ListAsync)
            .Produces<FileSystemListingVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> GetRootsAsync(IFileSystemBrowserService browser)
    {
        var roots = await browser.GetAllowedRootsAsync();
        return Results.Ok(roots);
    }

    private static async Task<IResult> ListAsync([FromQuery] string path, IFileSystemBrowserService browser)
    {
        try
        {
            var listing = await browser.ListAsync(path);
            return Results.Ok(listing);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { Message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { Message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (DirectoryNotFoundException ex)
        {
            return Results.NotFound(new { Message = ex.Message });
        }
    }
}
