using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Vora.Api.Extensions;
using Vora.Application.Auth;
using Vora.Application.Auth.External;
using Vora.Application.Auth.ViewModels;
using Vora.Domain.Enums;

namespace Vora.Api.Endpoints;

public class ExternalExchangeRequestDto
{
    public required string Code { get; set; }
}

public static class ExternalAuthEndpoints
{
    public static RouteGroupBuilder MapExternalAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Authentication");

        group.MapGet("/providers", GetProvidersAsync)
            .WithName("ListSignInProviders")
            .Produces<AuthProvidersVM>(StatusCodes.Status200OK)
            .AllowAnonymous();
        group.MapPost("/external/exchange", ExchangeAsync)
            .WithName("ExchangeExternalSignIn")
            .Produces<Vora.Application.Auth.Dtos.AuthResponseDto>(StatusCodes.Status200OK)
            .AllowAnonymous()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);
        group.MapGet("/identities", ListIdentitiesAsync)
            .WithName("ListLinkedSignIns")
            .Produces<IReadOnlyList<LinkedIdentityVM>>(StatusCodes.Status200OK)
            .RequireAuthorization();
        group.MapDelete("/identities/{id:guid}", UnlinkIdentityAsync)
            .WithName("UnlinkSignIn")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization();

        var admin = routes.MapGroup("/api/admin/auth-providers").WithTags("Sign-in providers").RequireAuthorization("AdminOnly");

        admin.MapGet("/", ListProvidersAsync)
            .WithName("ListAuthProviders")
            .Produces<IReadOnlyList<AuthProviderAdminVM>>(StatusCodes.Status200OK);
        admin.MapGet("/{id:guid}", GetProviderAsync)
            .WithName("GetAuthProvider")
            .Produces<AuthProviderAdminVM>(StatusCodes.Status200OK);
        admin.MapPost("/", CreateProviderAsync)
            .WithName("CreateAuthProvider")
            .Produces<AuthProviderAdminVM>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPut("/{id:guid}", UpdateProviderAsync)
            .WithName("UpdateAuthProvider")
            .Produces<AuthProviderAdminVM>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapDelete("/{id:guid}", DeleteProviderAsync)
            .WithName("DeleteAuthProvider")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var settings = routes.MapGroup("/api/admin/auth-settings").WithTags("Sign-in providers").RequireAuthorization("AdminOnly");

        settings.MapGet("/", GetSettingsAsync)
            .WithName("GetAuthSettings")
            .Produces<AuthSettingsVM>(StatusCodes.Status200OK);
        settings.MapPut("/", UpdateSettingsAsync)
            .WithName("UpdateAuthSettings")
            .Produces<AuthSettingsVM>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> GetProvidersAsync(IAuthProviderManager manager) =>
        Results.Ok(await manager.GetPublicAsync());

    private static async Task<IResult> ExchangeAsync([FromBody] ExternalExchangeRequestDto request, IExternalSignInCodeStore codes, IAuthManager manager, HttpContext httpContext)
    {
        var grant = codes.Redeem(request.Code);
        if (grant is null)
        {
            return Results.Unauthorized();
        }

        var result = await manager.SignInExternalAsync(grant.UserId);
        if (result is null)
        {
            return Results.Unauthorized();
        }

        result.RefreshToken = await manager.IssueRefreshTokenAsync(result.UserId, httpContext.Request.Headers["X-Vora-Device-Id"].FirstOrDefault(), AuthSignInMethod.External);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListIdentitiesAsync(ClaimsPrincipal user, IExternalIdentityManager manager)
    {
        var accountId = user.GetAccountId();
        return accountId is null ? Results.Unauthorized() : Results.Ok(await manager.ListAsync(accountId.Value));
    }

    private static async Task<IResult> UnlinkIdentityAsync(Guid id, ClaimsPrincipal user, IExternalIdentityManager manager)
    {
        var accountId = user.GetAccountId();
        if (accountId is null)
        {
            return Results.Unauthorized();
        }

        return await manager.UnlinkAsync(accountId.Value, id) switch
        {
            UnlinkOutcome.Unlinked => Results.NoContent(),
            UnlinkOutcome.LastWayIn => Results.Problem(detail: "That is the only way you can sign in. Set a password or link another sign-in first.", statusCode: StatusCodes.Status409Conflict),
            _ => Results.NotFound()
        };
    }

    private static async Task<IResult> ListProvidersAsync(IAuthProviderManager manager, HttpContext httpContext) =>
        Results.Ok(await manager.ListAsync(Origin(httpContext)));

    private static async Task<IResult> GetProviderAsync(Guid id, IAuthProviderManager manager, HttpContext httpContext)
    {
        var provider = await manager.GetAsync(id, Origin(httpContext));
        return provider is null ? Results.NotFound() : Results.Ok(provider);
    }

    private static async Task<IResult> CreateProviderAsync([FromBody] SaveAuthProviderRequest request, IAuthProviderManager manager, HttpContext httpContext) =>
        SaveResult(await manager.CreateAsync(request, Origin(httpContext)));

    private static async Task<IResult> UpdateProviderAsync(Guid id, [FromBody] SaveAuthProviderRequest request, IAuthProviderManager manager, HttpContext httpContext) =>
        SaveResult(await manager.UpdateAsync(id, request, Origin(httpContext)));

    private static async Task<IResult> DeleteProviderAsync(Guid id, [FromQuery] bool? force, IAuthProviderManager manager)
    {
        var result = await manager.DeleteAsync(id, force ?? false);
        return result.Outcome switch
        {
            AuthProviderDeleteOutcome.Deleted => Results.NoContent(),
            AuthProviderDeleteOutcome.WouldStrandUsers => Results.Problem(
                detail: $"{result.StrandedUsers} account(s) can only sign in through this provider and would be locked out. Remove it anyway with force.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.NotFound()
        };
    }

    private static async Task<IResult> GetSettingsAsync(IAuthProviderManager manager, HttpContext httpContext) =>
        Results.Ok(await manager.GetSettingsAsync(Origin(httpContext)));

    private static async Task<IResult> UpdateSettingsAsync([FromBody] UpdateAuthSettingsRequest request, IAuthProviderManager manager, HttpContext httpContext)
    {
        var result = await manager.UpdateSettingsAsync(request, Origin(httpContext));
        return result.Saved ? Results.Ok(result.Settings) : Results.Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult SaveResult(AuthProviderSaveResult result) => result.Outcome switch
    {
        AuthProviderSaveOutcome.Saved => Results.Ok(result.Provider),
        AuthProviderSaveOutcome.SlugInUse => Results.Problem(detail: result.Error, statusCode: StatusCodes.Status409Conflict),
        AuthProviderSaveOutcome.NotFound => Results.NotFound(),
        _ => Results.Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest)
    };

    private static string Origin(HttpContext httpContext) =>
        $"{httpContext.Request.Scheme}://{httpContext.Request.Host.Value}";
}
