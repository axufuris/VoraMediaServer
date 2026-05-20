using Microsoft.AspNetCore.Mvc;
using Vora.Application.Auth;
using Vora.Application.Settings;

namespace Vora.Api.Endpoints;

public class SetupRequestDto
{
    public required string Email { get; set; }
    public required string Password { get; set; }
    public required string DisplayName { get; set; }
}

public class RegisterRequestDto : SetupRequestDto
{
    public string? SecretCode { get; set; }
}

public class LoginRequestDto
{
    public required string Email { get; set; }
    public required string Password { get; set; }
}

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Authentication");

        group.MapGet("/setup-status", GetSetupStatusAsync);
        group.MapPost("/setup", ClaimServerAsync);
        group.MapPost("/login", LoginAsync);
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/exchange-profile-token", ExchangeProfileTokenAsync);

        group.MapPost("/invite-code", GenerateInviteCodeAsync)
            .RequireAuthorization("AdminOnly");

        return group;
    }

    private static async Task<IResult> GetSetupStatusAsync(IAuthManager authManager, ISystemSettingsManager settingsManager)
    {
        var status = await authManager.GetSetupStatusAsync();
        var settings = await settingsManager.GetServerSettingsAsync();
        return Results.Ok(new
        {
            status.IsClaimed,
            RegistrationMode = (int)status.Mode,
            settings.ServerName
        });
    }

    private static async Task<IResult> ClaimServerAsync([FromBody] SetupRequestDto request, IAuthManager manager)
    {
        try
        {
            var result = await manager.ClaimServerAsync(request.Email, request.Password, request.DisplayName);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { ex.Message });
        }
    }

    private static async Task<IResult> LoginAsync([FromBody] LoginRequestDto request, IAuthManager manager)
    {
        var result = await manager.LoginAsync(request.Email, request.Password);
        return result != null ? Results.Ok(result) : Results.Unauthorized();
    }

    private static async Task<IResult> RegisterAsync([FromBody] RegisterRequestDto request, IAuthManager manager)
    {
        try
        {
            var result = await manager.RegisterAsync(request.Email, request.Password, request.DisplayName, request.SecretCode);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { ex.Message });
        }
    }

    private static async Task<IResult> ExchangeProfileTokenAsync([FromQuery] Guid accountId, [FromQuery] Guid profileId, IAuthManager manager)
    {
        var token = await manager.GenerateProfileTokenAsync(accountId, profileId);
        return token != null ? Results.Ok(new { Token = token }) : Results.Unauthorized();
    }

    private static async Task<IResult> GenerateInviteCodeAsync(IAuthManager manager)
    {
        var code = await manager.GenerateInviteCodeAsync();
        return Results.Ok(new { Code = code });
    }
}
