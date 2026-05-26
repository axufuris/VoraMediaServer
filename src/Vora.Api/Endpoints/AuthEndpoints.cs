using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Auth;
using Vora.Application.Auth.ViewModels;
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
    public string? InviteToken { get; set; }
}

public class LoginRequestDto
{
    public required string Email { get; set; }
    public required string Password { get; set; }
}

public class ForgotPasswordRequestDto
{
    public required string Email { get; set; }
}

public class ResetPasswordRequestDto
{
    public required string Token { get; set; }
    public required string NewPassword { get; set; }
}

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Authentication");

        group.MapGet("/setup-status", GetSetupStatusAsync);
        group.MapPost("/setup", ClaimServerAsync);
        group.MapPost("/login", LoginAsync)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);
        group.MapPost("/register", RegisterAsync)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);
        group.MapPost("/exchange-profile-token", ExchangeProfileTokenAsync)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthBurst);
        group.MapPost("/forgot-password", RequestPasswordResetAsync)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);
        group.MapPost("/reset-password", ConfirmPasswordResetAsync)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);

        group.MapPost("/invitations/validate", ValidateInvitationAsync)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthBurst);

        group.MapPost("/invite-code", GenerateInviteCodeAsync)
            .RequireAuthorization("AdminOnly");

        group.MapGet("/invitations", ListInvitationsAsync)
            .RequireAuthorization("AdminOnly");
        group.MapPost("/invitations", CreateInvitationAsync)
            .RequireAuthorization("AdminOnly");
        group.MapDelete("/invitations/{id:guid}", RevokeInvitationAsync)
            .RequireAuthorization("AdminOnly");

        return group;
    }

    private static async Task<IResult> GetSetupStatusAsync(IAuthManager authManager, ISystemSettingsManager settingsManager, ISystemSettingsRepository settingsRepo)
    {
        var status = await authManager.GetSetupStatusAsync();
        var settings = await settingsManager.GetServerSettingsAsync();
        var serverSettings = await settingsRepo.GetSettingsAsync();
        return Results.Ok(new
        {
            status.IsClaimed,
            RegistrationMode = (int)status.Mode,
            settings.ServerName,
            EmailEnabled = serverSettings.EmailEnabled
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
            var result = await manager.RegisterAsync(request.Email, request.Password, request.DisplayName, request.SecretCode, request.InviteToken);
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

    private static async Task<IResult> RequestPasswordResetAsync([FromBody] ForgotPasswordRequestDto request, IAuthManager manager, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var origin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host.Value}";
        await manager.RequestPasswordResetAsync(request.Email, origin, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmPasswordResetAsync([FromBody] ResetPasswordRequestDto request, IAuthManager manager, CancellationToken cancellationToken)
    {
        var result = await manager.ConfirmPasswordResetAsync(request.Token, request.NewPassword, cancellationToken);
        return result switch
        {
            PasswordResetResult.Success => Results.NoContent(),
            PasswordResetResult.PasswordRejected => Results.BadRequest(new { Message = "Password must be at least 8 characters." }),
            _ => Results.BadRequest(new { Message = "Invalid or expired reset token." })
        };
    }

    private static async Task<IResult> ListInvitationsAsync(IInvitationManager manager)
    {
        var invites = await manager.GetActiveInvitationsAsync();
        return Results.Ok(invites);
    }

    private static async Task<IResult> CreateInvitationAsync([FromBody] CreateInvitationRequest request, HttpContext httpContext, IInvitationManager manager, CancellationToken cancellationToken)
    {
        var invitedBy = httpContext.User.GetAccountId();
        var result = await manager.CreateInvitationAsync(request.Email, request.ExpiresInDays, invitedBy, cancellationToken);

        return result.Outcome switch
        {
            InvitationCreateOutcome.Created => Results.Ok(new CreateInvitationResponse
            {
                Invitation = result.Invitation!,
                EmailSent = result.EmailSent,
                Message = result.EmailSent ? null : result.ErrorMessage
            }),
            InvitationCreateOutcome.EmailAlreadyRegistered => Results.Conflict(new { Message = result.ErrorMessage }),
            _ => Results.BadRequest(new { Message = result.ErrorMessage })
        };
    }

    private static async Task<IResult> RevokeInvitationAsync(Guid id, IInvitationManager manager)
    {
        var removed = await manager.RevokeAsync(id);
        return removed ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> ValidateInvitationAsync([FromBody] ValidateInvitationRequest request, IInvitationManager manager)
    {
        var ticket = await manager.ValidateTokenAsync(request.Token);
        if (ticket is null)
        {
            return Results.NotFound(new { Message = "Invitation is invalid or has expired." });
        }

        return Results.Ok(new ValidateInvitationResponse
        {
            Email = ticket.Email,
            ExpiresAt = ticket.ExpiresAt
        });
    }
}
