using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Vora.Api.Extensions;
using Vora.Application.Auth.Pairing;

namespace Vora.Api.Endpoints;

public class DevicePairingPollRequestDto
{
    public required string DeviceCode { get; set; }
}

public class DevicePairingDecisionRequestDto
{
    public required string UserCode { get; set; }
}

public class DevicePairingDecisionVM
{
    public required string Status { get; set; }
}

public static class DevicePairingEndpoints
{
    public static RouteGroupBuilder MapDevicePairingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth/device").WithTags("Device pairing");

        group.MapPost("/start", StartAsync)
            .WithName("StartDevicePairing")
            .Produces<DevicePairingStartVM>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AllowAnonymous()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);
        group.MapPost("/poll", PollAsync)
            .WithName("PollDevicePairing")
            .Produces<DevicePairingPollVM>(StatusCodes.Status200OK)
            .AllowAnonymous()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthPoll);
        group.MapGet("/lookup", LookupAsync)
            .WithName("LookUpDevicePairing")
            .Produces<DevicePairingRequestVM>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireAuthorization()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);
        group.MapPost("/approve", ApproveAsync)
            .WithName("ApproveDevicePairing")
            .Produces<DevicePairingDecisionVM>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireAuthorization()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);
        group.MapPost("/deny", DenyAsync)
            .WithName("DenyDevicePairing")
            .Produces<DevicePairingDecisionVM>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireAuthorization()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);

        return group;
    }

    private static async Task<IResult> StartAsync(IDevicePairingManager manager, HttpContext httpContext)
    {
        var identity = httpContext.Request.ReadDeviceIdentity();
        var device = new DevicePairingDevice(
            httpContext.Request.ReadDeviceId(),
            identity.ClientName,
            identity.DeviceName,
            identity.DeviceType,
            identity.OperatingSystem,
            httpContext.GetClientAddress());

        var result = await manager.StartAsync(device, $"{httpContext.Request.Scheme}://{httpContext.Request.Host.Value}");
        return result.Pairing is { } pairing
            ? Results.Ok(pairing)
            : Results.Problem(title: "Device blocked", detail: "This device has been blocked by the server administrator.", statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> PollAsync([FromBody] DevicePairingPollRequestDto request, IDevicePairingManager manager) =>
        Results.Ok(await manager.PollAsync(request.DeviceCode));

    private static async Task<IResult> LookupAsync([FromQuery] string? userCode, ClaimsPrincipal user, IDevicePairingManager manager, HttpContext httpContext)
    {
        var accountId = user.GetAccountId();
        if (accountId is null)
        {
            return Results.Unauthorized();
        }

        var result = await manager.LookupAsync(userCode, accountId.Value, httpContext.GetClientAddress());
        return result.Outcome switch
        {
            DevicePairingLookupOutcome.Found => Results.Ok(result.Request),
            DevicePairingLookupOutcome.LockedOut => TooManyWrongCodes(),
            _ => NotFound()
        };
    }

    private static Task<IResult> ApproveAsync([FromBody] DevicePairingDecisionRequestDto request, ClaimsPrincipal user, IDevicePairingManager manager) =>
        DecideAsync(request, user, manager, approve: true);

    private static Task<IResult> DenyAsync([FromBody] DevicePairingDecisionRequestDto request, ClaimsPrincipal user, IDevicePairingManager manager) =>
        DecideAsync(request, user, manager, approve: false);

    private static async Task<IResult> DecideAsync(DevicePairingDecisionRequestDto request, ClaimsPrincipal user, IDevicePairingManager manager, bool approve)
    {
        var accountId = user.GetAccountId();
        if (accountId is null)
        {
            return Results.Unauthorized();
        }

        return await manager.DecideAsync(request.UserCode, accountId.Value, approve) switch
        {
            DevicePairingDecideOutcome.Approved => Results.Ok(new DevicePairingDecisionVM { Status = "approved" }),
            DevicePairingDecideOutcome.Denied => Results.Ok(new DevicePairingDecisionVM { Status = "denied" }),
            DevicePairingDecideOutcome.AlreadyDecided => Results.Problem(title: "Already answered", detail: "That sign-in has already been answered.", statusCode: StatusCodes.Status409Conflict),
            DevicePairingDecideOutcome.LockedOut => TooManyWrongCodes(),
            DevicePairingDecideOutcome.AccountNotAllowed => Results.Problem(title: "Password sign-in is turned off", detail: "Password sign-in is turned off on this server. Sign in with one of its sign-in providers to link a device.", statusCode: StatusCodes.Status403Forbidden),
            DevicePairingDecideOutcome.DeviceBlocked => Results.Problem(title: "Device blocked", detail: "This device has been blocked by the server administrator.", statusCode: StatusCodes.Status403Forbidden),
            _ => NotFound()
        };
    }

    private static IResult NotFound() =>
        Results.Problem(title: "Code not found", detail: "That code is not waiting for approval. Check it on the device's screen, or start again on the device.", statusCode: StatusCodes.Status404NotFound);

    private static IResult TooManyWrongCodes() =>
        Results.Problem(title: "Too many wrong codes", detail: "Too many codes that did not match. Wait a few minutes and try again.", statusCode: StatusCodes.Status429TooManyRequests);
}
