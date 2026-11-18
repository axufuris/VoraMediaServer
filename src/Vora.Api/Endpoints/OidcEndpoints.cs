using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Vora.Api.Extensions;
using Vora.Application.Auth.External.Oidc;
using Vora.Application.Auth.ViewModels;

namespace Vora.Api.Endpoints;

public static class OidcEndpoints
{
    public static RouteGroupBuilder MapOidcEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Authentication");

        group.MapGet("/oidc/{slug}/start", StartAsync)
            .WithName("StartOidcSignIn")
            .AllowAnonymous()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthBurst);
        group.MapGet("/oidc/{slug}/callback", CallbackAsync)
            .WithName("OidcCallback")
            .AllowAnonymous()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthBurst);
        group.MapPost("/oidc/{slug}/link", StartLinkAsync)
            .WithName("StartOidcLink")
            .Produces<StartLinkVM>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthBurst);
        group.MapGet("/external/pending/{handle}", GetPendingAsync)
            .WithName("GetPendingExternalSignIn")
            .Produces<ExternalPendingVM>(StatusCodes.Status200OK)
            .AllowAnonymous()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthBurst);
        group.MapPost("/external/complete", CompleteAsync)
            .WithName("CompleteExternalSignIn")
            .Produces<ExternalCompleteVM>(StatusCodes.Status200OK)
            .AllowAnonymous()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);

        routes.MapGroup("/api/admin/auth-providers").WithTags("Sign-in providers").RequireAuthorization("AdminOnly")
            .MapPost("/{id:guid}/test", TestAsync)
            .WithName("TestAuthProvider")
            .Produces<OidcTestResultVM>(StatusCodes.Status200OK);

        return group;
    }

    private static async Task<IResult> StartAsync(string slug, [FromQuery] string? returnTo, [FromQuery] string? invite, IOidcSignInManager manager, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await manager.StartAsync(slug, new OidcStartRequest(returnTo, invite, null), Origin(httpContext), cancellationToken);
        return Results.Redirect(result is { Outcome: OidcStartOutcome.Redirect, RedirectUrl: { } url } ? url : OidcRedirects.StartFailure(result));
    }

    private static async Task<IResult> CallbackAsync(string slug, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, IOidcSignInManager manager, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await manager.CompleteAsync(slug, new OidcCallbackParameters(code, state, error), Origin(httpContext), cancellationToken);
        return Results.Redirect(OidcRedirects.Destination(result));
    }

    private static async Task<IResult> StartLinkAsync(string slug, [FromBody] StartLinkRequest request, ClaimsPrincipal user, IOidcSignInManager manager, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var accountId = user.GetAccountId();
        if (accountId is null)
        {
            return Results.Unauthorized();
        }

        var result = await manager.StartAsync(slug, new OidcStartRequest(request.ReturnTo, null, accountId), Origin(httpContext), cancellationToken);
        return result is { Outcome: OidcStartOutcome.Redirect, RedirectUrl: { } url }
            ? Results.Ok(new StartLinkVM { Url = url })
            : Results.Problem(detail: "The sign-in provider is not available.", statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode });
    }

    private static IResult GetPendingAsync(string handle, IOidcSignInManager manager)
    {
        var pending = manager.DescribePending(handle);
        return pending is null ? Results.NotFound() : Results.Ok(pending);
    }

    private static async Task<IResult> CompleteAsync([FromBody] ExternalCompleteRequest request, IOidcSignInManager manager, CancellationToken cancellationToken) =>
        Results.Ok(await manager.ResumeAsync(request, cancellationToken));

    private static async Task<IResult> TestAsync(Guid id, IOidcSignInManager manager, CancellationToken cancellationToken)
    {
        var result = await manager.TestAsync(id, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static string Origin(HttpContext httpContext) =>
        $"{httpContext.Request.Scheme}://{httpContext.Request.Host.Value}";
}
