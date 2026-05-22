using Microsoft.AspNetCore.Mvc;
using Vora.Application.LibraryMigration;
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
    public required string ConnectionUri { get; set; }
}

public class RunLibraryMigrationRequest
{
    public required string AccessToken { get; set; }
    public required string ServerClientIdentifier { get; set; }
    public required string ServerName { get; set; }
    public required string ConnectionUri { get; set; }
    public bool IncludeWatchState { get; set; } = true;
    public bool IncludeRatings { get; set; } = true;
    public required List<string> LibrarySectionKeys { get; set; }
    public required List<RunLibraryMigrationMapping> Mappings { get; set; }
}

public class RunLibraryMigrationMapping
{
    public required string AccountId { get; set; }
    public required string AccountName { get; set; }
    public required Guid ProfileId { get; set; }
    public required string ProfileName { get; set; }
    public string? Pin { get; set; }
}

public static class LibraryMigrationEndpoints
{
    public static RouteGroupBuilder MapLibraryMigrationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/library-migration").WithTags("Library Migration").RequireAuthorization("AdminOnly");

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

        group.MapPost("/providers/{providerId}/run", RunMigrationAsync)
            .Produces<LibraryMigrationJobVM>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest);

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
        if (string.IsNullOrWhiteSpace(request.ConnectionUri))
        {
            return Results.BadRequest(new { error = "connectionUri is required." });
        }

        try
        {
            var libraries = await manager.ListLibrariesAsync(providerId, request.AccessToken, request.ConnectionUri, cancellationToken);
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

    private static IResult RunMigrationAsync(string providerId, [FromBody] RunLibraryMigrationRequest request, ILibraryMigrationJobRunner runner)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Results.BadRequest(new { error = "accessToken is required." });
        }
        if (string.IsNullOrWhiteSpace(request.ConnectionUri))
        {
            return Results.BadRequest(new { error = "connectionUri is required." });
        }
        if (request.Mappings.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one mapping is required." });
        }
        if (!request.IncludeWatchState && !request.IncludeRatings)
        {
            return Results.BadRequest(new { error = "At least one of IncludeWatchState or IncludeRatings must be true." });
        }
        if (request.LibrarySectionKeys.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one library section must be selected." });
        }

        var input = new LibraryMigrationJobInput
        {
            ProviderId = providerId,
            AdminAccessToken = request.AccessToken,
            ServerClientIdentifier = request.ServerClientIdentifier,
            ServerName = request.ServerName,
            ConnectionUri = request.ConnectionUri,
            IncludeWatchState = request.IncludeWatchState,
            IncludeRatings = request.IncludeRatings,
            LibrarySectionKeys = request.LibrarySectionKeys,
            Mappings = request.Mappings
                .Select(m => new LibraryMigrationMappingInput
                {
                    AccountId = m.AccountId,
                    AccountName = m.AccountName,
                    ProfileId = m.ProfileId,
                    ProfileName = m.ProfileName,
                    Pin = string.IsNullOrEmpty(m.Pin) ? null : m.Pin
                })
                .ToList()
        };

        var job = runner.StartJob(input);
        return Results.Accepted($"/api/library-migration/jobs/{job.JobId}", job);
    }

    private static IResult GetJob(Guid jobId, ILibraryMigrationJobRunner runner)
    {
        var job = runner.GetJob(jobId);
        return job is null ? Results.NotFound() : Results.Ok(job);
    }
}
