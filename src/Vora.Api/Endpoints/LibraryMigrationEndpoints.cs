using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.LibraryMigration;
using Vora.Application.LibraryMigration.Requests;
using Vora.Application.LibraryMigration.ViewModels;

namespace Vora.Api.Endpoints;

public class ListSyncServersRequest
{
    public required string AccessToken { get; set; }
}

public class ListSyncAccountsRequest
{
    public required string AccessToken { get; set; }
}

public class ListSyncLibrariesRequest
{
    public required string AccessToken { get; set; }
    public required string ServerClientIdentifier { get; set; }
}

public static class LibraryMigrationEndpoints
{
    public static RouteGroupBuilder MapLibraryMigrationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/library-import").WithTags("Library Import").RequireAuthorization();

        group.MapGet("/providers", GetProviders)
            .Produces<IEnumerable<LibrarySyncProviderVM>>(StatusCodes.Status200OK);

        group.MapPost("/providers/{providerId}/pin", CreatePinAsync)
            .Produces<LibrarySyncPinVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/providers/{providerId}/pin/{pinId}", PollPinAsync)
            .Produces<LibrarySyncPinStatusVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/providers/{providerId}/servers", ListServersAsync)
            .Produces<IReadOnlyList<RemoteServerVM>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/providers/{providerId}/accounts", ListAccountsAsync)
            .Produces<IReadOnlyList<RemoteAccountVM>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/providers/{providerId}/libraries", ListLibrariesAsync)
            .Produces<IReadOnlyList<RemoteLibraryVM>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/providers/{providerId}/run", StartImportAsync)
            .Produces<LibraryMigrationJobVM>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/jobs/{jobId:guid}", GetJob)
            .Produces<LibraryMigrationJobVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult GetProviders(ILibraryMigrationManager manager)
    {
        var providers = manager.GetAvailableProviders();
        return Results.Ok(providers);
    }

    private static async Task<IResult> CreatePinAsync(string providerId, ILibraryMigrationManager manager, CancellationToken cancellationToken)
    {
        try
        {
            var pin = await manager.CreatePinAsync(providerId, cancellationToken);
            return Results.Ok(pin);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> PollPinAsync(string providerId, string pinId, ILibraryMigrationManager manager, CancellationToken cancellationToken)
    {
        try
        {
            var status = await manager.PollPinAsync(providerId, pinId, cancellationToken);
            return Results.Ok(status);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> ListServersAsync(string providerId, [FromBody] ListSyncServersRequest request, ILibraryMigrationManager manager, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Results.BadRequest(new { error = "accessToken is required." });
        }

        try
        {
            var servers = await manager.ListServersAsync(providerId, request.AccessToken, cancellationToken);
            return Results.Ok(servers);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ListAccountsAsync(string providerId, [FromBody] ListSyncAccountsRequest request, ILibraryMigrationManager manager, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Results.BadRequest(new { error = "accessToken is required." });
        }

        try
        {
            var accounts = await manager.ListAccountsAsync(providerId, request.AccessToken, cancellationToken);
            return Results.Ok(accounts);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ListLibrariesAsync(string providerId, [FromBody] ListSyncLibrariesRequest request, ILibraryMigrationManager manager, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Results.BadRequest(new { error = "accessToken is required." });
        }
        if (string.IsNullOrWhiteSpace(request.ServerClientIdentifier))
        {
            return Results.BadRequest(new { error = "serverClientIdentifier is required." });
        }

        try
        {
            var libraries = await manager.ListLibrariesAsync(providerId, request.AccessToken, request.ServerClientIdentifier, cancellationToken);
            return Results.Ok(libraries);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> StartImportAsync(string providerId, [FromBody] StartLibraryImportRequest request, ClaimsPrincipal user, ILibraryMigrationManager manager)
    {
        var accountId = user.GetAccountId();
        var profileId = user.GetProfileId();
        if (accountId is null || profileId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var job = await manager.StartImportAsync(providerId, request, accountId.Value, profileId.Value, user.IsAdmin());
            return Results.Accepted($"/api/library-import/jobs/{job.JobId}", job);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static IResult GetJob(Guid jobId, ClaimsPrincipal user, ILibraryMigrationJobRunner runner)
    {
        var accountId = user.GetAccountId();
        var job = accountId is null ? null : runner.GetJob(jobId, accountId.Value);
        return job is null ? Results.NotFound() : Results.Ok(job);
    }
}
