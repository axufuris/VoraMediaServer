using Microsoft.AspNetCore.Mvc;
using Vora.Application.Email;
using Vora.Application.Email.ViewModels;
using Vora.Domain.Enums;

namespace Vora.Api.Endpoints;

public static class EmailEndpoints
{
    public static RouteGroupBuilder MapEmailEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/email").WithTags("Email").RequireAuthorization("AdminOnly");

        group.MapGet("/settings", GetSettingsAsync)
            .Produces<EmailSettingsVM>(StatusCodes.Status200OK);

        group.MapPut("/settings", UpdateSettingsAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/test", SendTestAsync)
            .Produces<SendTestEmailResponse>(StatusCodes.Status200OK);

        group.MapGet("/templates", ListTemplatesAsync)
            .Produces<IReadOnlyList<EmailTemplateSummaryVM>>(StatusCodes.Status200OK);

        group.MapGet("/templates/{key}", GetTemplateAsync)
            .Produces<EmailTemplateDetailVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/templates/{key}", UpdateTemplateAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/templates/{key}", DeleteTemplateAsync)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/log", GetLogAsync)
            .Produces<IReadOnlyList<EmailDeliveryLogVM>>(StatusCodes.Status200OK);

        return group;
    }

    private static async Task<IResult> GetSettingsAsync(IEmailSettingsManager manager)
    {
        var settings = await manager.GetSettingsAsync();
        return Results.Ok(settings);
    }

    private static async Task<IResult> UpdateSettingsAsync([FromBody] UpdateEmailSettingsRequest request, IEmailSettingsManager manager)
    {
        await manager.UpdateSettingsAsync(request);
        return Results.NoContent();
    }

    private static async Task<IResult> SendTestAsync([FromBody] SendTestEmailRequest request, IEmailSettingsManager manager, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ToAddress))
        {
            return Results.BadRequest(new SendTestEmailResponse { Success = false, Message = "A recipient address is required." });
        }

        var result = await manager.SendTestAsync(request.ToAddress, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListTemplatesAsync(IEmailTemplateManager manager, CancellationToken cancellationToken)
    {
        var templates = await manager.ListAsync(cancellationToken);
        return Results.Ok(templates);
    }

    private static async Task<IResult> GetTemplateAsync(string key, IEmailTemplateManager manager, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<EmailTemplateKey>(key, ignoreCase: true, out var parsedKey))
        {
            return Results.NotFound();
        }

        var detail = await manager.GetAsync(parsedKey, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> UpdateTemplateAsync(string key, [FromBody] UpdateEmailTemplateRequest request, IEmailTemplateManager manager, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<EmailTemplateKey>(key, ignoreCase: true, out var parsedKey))
        {
            return Results.NotFound();
        }

        await manager.UpdateAsync(parsedKey, request, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteTemplateAsync(string key, IEmailTemplateManager manager, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<EmailTemplateKey>(key, ignoreCase: true, out var parsedKey))
        {
            return Results.NoContent();
        }

        await manager.DeleteAsync(parsedKey, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetLogAsync([FromQuery] int? take, IEmailDeliveryLogRepository logRepo, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(take ?? 50, 1, 200);
        var rows = await logRepo.GetRecentAsync(limit, cancellationToken);
        var vms = rows.Select(EmailDeliveryLogVM.From).ToList();
        return Results.Ok(vms);
    }
}
