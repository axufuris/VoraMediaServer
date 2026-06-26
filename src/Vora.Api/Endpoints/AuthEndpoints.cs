using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
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

public class ConfirmEmailChangeRequestDto
{
    public required string Token { get; set; }
}

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Authentication");

        group.MapGet("/setup-status", GetSetupStatusAsync)
            .WithName("GetSetupStatus")
            .Produces<SetupStatusVM>(StatusCodes.Status200OK);
        group.MapPost("/setup", ClaimServerAsync)
            .WithName("ClaimServer")
            .Produces<Vora.Application.Auth.Dtos.AuthResponseDto>(StatusCodes.Status200OK);
        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .Produces<Vora.Application.Auth.Dtos.AuthResponseDto>(StatusCodes.Status200OK)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);
        group.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .Produces<Vora.Application.Auth.Dtos.AuthResponseDto>(StatusCodes.Status200OK)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);
        group.MapPost("/exchange-profile-token", ExchangeProfileTokenAsync)
            .WithName("ExchangeProfileToken")
            .Produces<ExchangeProfileTokenResponse>(StatusCodes.Status200OK)
            .RequireAuthorization()
            .RequireRateLimiting(VoraRateLimitPolicies.AuthBurst);
        group.MapPost("/forgot-password", RequestPasswordResetAsync)
            .WithName("RequestPasswordReset")
            .Produces(StatusCodes.Status204NoContent)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);
        group.MapPost("/reset-password", ConfirmPasswordResetAsync)
            .WithName("ConfirmPasswordReset")
            .Produces(StatusCodes.Status204NoContent)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);
        group.MapPost("/confirm-email-change", ConfirmEmailChangeAsync)
            .WithName("ConfirmEmailChange")
            .Produces(StatusCodes.Status204NoContent)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthStrict);

        group.MapPost("/invitations/validate", ValidateInvitationAsync)
            .WithName("ValidateInvitation")
            .Produces<ValidateInvitationResponse>(StatusCodes.Status200OK)
            .RequireRateLimiting(VoraRateLimitPolicies.AuthBurst);

        group.MapPost("/invite-code", GenerateInviteCodeAsync)
            .WithName("GenerateInviteCode")
            .Produces<GenerateInviteCodeResponse>(StatusCodes.Status200OK)
            .RequireAuthorization("AdminOnly");

        group.MapGet("/invitations", ListInvitationsAsync)
            .WithName("ListInvitations")
            .RequireAuthorization("AdminOnly");
        group.MapPost("/invitations", CreateInvitationAsync)
            .WithName("CreateInvitation")
            .RequireAuthorization("AdminOnly");
        group.MapDelete("/invitations/{id:guid}", RevokeInvitationAsync)
            .WithName("RevokeInvitation")
            .RequireAuthorization("AdminOnly");

        return group;
    }

    private static async Task<IResult> GetSetupStatusAsync(IAuthManager authManager, ISystemSettingsManager settingsManager, ISystemSettingsRepository settingsRepo)
    {
        var status = await authManager.GetSetupStatusAsync();
        var settings = await settingsManager.GetServerSettingsAsync();
        var serverSettings = await settingsRepo.GetSettingsAsync();
        return Results.Ok(new SetupStatusVM
        {
            IsClaimed = status.IsClaimed,
            RegistrationMode = (int)status.Mode,
            ServerName = settings.ServerName,
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
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
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
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> ExchangeProfileTokenAsync([FromQuery] Guid accountId, [FromQuery] Guid profileId, ClaimsPrincipal user, IAuthManager manager)
    {
        var callerAccountId = user.GetAccountId();
        if (callerAccountId == null || callerAccountId.Value != accountId)
        {
            return Results.Forbid();
        }

        var token = await manager.GenerateProfileTokenAsync(callerAccountId.Value, profileId);
        return token != null
            ? Results.Ok(new ExchangeProfileTokenResponse { Token = token })
            : Results.Unauthorized();
    }


    private static async Task<IResult> GenerateInviteCodeAsync(IAuthManager manager)
    {
        var code = await manager.GenerateInviteCodeAsync();
        return Results.Ok(new GenerateInviteCodeResponse { Code = code });
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

    private static async Task<IResult> ConfirmEmailChangeAsync([FromBody] ConfirmEmailChangeRequestDto request, IAuthManager manager, CancellationToken cancellationToken)
    {
        var result = await manager.ConfirmEmailChangeAsync(request.Token, cancellationToken);
        return result switch
        {
            EmailChangeConfirmResult.Success => Results.NoContent(),
            EmailChangeConfirmResult.AlreadyInUse => Results.Conflict(new { Message = "That email address is already in use." }),
            _ => Results.BadRequest(new { Message = "Invalid or expired confirmation link." })
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
