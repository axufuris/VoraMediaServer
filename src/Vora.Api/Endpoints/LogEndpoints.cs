using Microsoft.AspNetCore.Mvc;
using Vora.Application.Logging;
using Vora.Application.Logging.ViewModels;

namespace Vora.Api.Endpoints;

public static class LogEndpoints
{
    public static RouteGroupBuilder MapLogEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/logs")
            .WithTags("Admin", "Logs")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", QueryAsync)
            .Produces<LogQueryResultVM>(StatusCodes.Status200OK);

        group.MapGet("/export", ExportAsync);

        group.MapGet("/categories", GetCategoriesAsync)
            .Produces<List<string>>(StatusCodes.Status200OK);

        group.MapGet("/levels", GetLevelsAsync)
            .Produces<LogLevelStateVM>(StatusCodes.Status200OK);

        group.MapPut("/levels/{category}", SetLevelAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapDelete("/levels/{category}", ClearLevelAsync)
            .Produces(StatusCodes.Status204NoContent);

        return group;
    }

    private static IResult QueryAsync(
        ILogManager manager,
        [FromQuery(Name = "levels")] string? levelsCsv,
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] DateTime? sinceUtc,
        [FromQuery] DateTime? untilUtc,
        [FromQuery] long? beforeId,
        [FromQuery] int? limit)
    {
        var request = BuildRequest(levelsCsv, category, search, sinceUtc, untilUtc, beforeId, limit);
        return Results.Ok(manager.Query(request));
    }

    private static IResult ExportAsync(
        ILogManager manager,
        [FromQuery] string format,
        [FromQuery(Name = "levels")] string? levelsCsv,
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] DateTime? sinceUtc,
        [FromQuery] DateTime? untilUtc)
    {
        var request = BuildRequest(levelsCsv, category, search, sinceUtc, untilUtc, beforeId: null, limit: 100_000);
        var stream = manager.Export(request, format, out var contentType, out var fileName);
        return Results.File(stream, contentType, fileName);
    }

    private static IResult GetCategoriesAsync(ILogManager manager) =>
        Results.Ok(manager.GetKnownCategories());

    private static IResult GetLevelsAsync(ILogManager manager) =>
        Results.Ok(manager.GetLevelState());

    private static IResult SetLevelAsync(string category, [FromBody] SetLevelRequest body, ILogManager manager)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Results.BadRequest(new { Message = "Category is required." });
        }
        manager.SetLevel(category, body.Level);
        return Results.NoContent();
    }

    private static IResult ClearLevelAsync(string category, ILogManager manager)
    {
        manager.ClearOverride(category);
        return Results.NoContent();
    }

    private static LogQueryRequest BuildRequest(
        string? levelsCsv,
        string? category,
        string? search,
        DateTime? sinceUtc,
        DateTime? untilUtc,
        long? beforeId,
        int? limit)
    {
        List<VoraLogLevel>? levels = null;
        if (!string.IsNullOrWhiteSpace(levelsCsv))
        {
            levels = levelsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(token => Enum.TryParse<VoraLogLevel>(token, ignoreCase: true, out var parsed)
                    ? (VoraLogLevel?)parsed
                    : null)
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .ToList();
        }

        return new LogQueryRequest
        {
            Levels = levels,
            CategoryPrefix = category,
            Search = search,
            SinceUtc = sinceUtc,
            UntilUtc = untilUtc,
            BeforeId = beforeId,
            Limit = limit ?? 500
        };
    }
}
