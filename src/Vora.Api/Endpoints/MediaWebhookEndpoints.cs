using System.Text;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Webhooks;

namespace Vora.Api.Endpoints;

public static class MediaWebhookEndpoints
{
    private const int MaxBodyBytes = 1_048_576;

    public static IEndpointRouteBuilder MapMediaWebhookEndpoints(this IEndpointRouteBuilder routes)
    {
        var receive = routes.MapGroup("/api/webhooks")
            .WithTags("Media Webhooks")
            .AllowAnonymous()
            .RequireRateLimiting(VoraRateLimitPolicies.WebhookReceive);

        receive.MapMethods("/{connectionId}", new[] { HttpMethods.Post, HttpMethods.Put }, ReceiveAsync)
            .ExcludeFromDescription();

        var admin = routes.MapGroup("/api/admin/webhooks")
            .WithTags("Media Webhooks (Admin)")
            .RequireAuthorization("AdminOnly");

        admin.MapGet("/providers", GetProviders)
            .Produces<List<WebhookProviderVM>>(StatusCodes.Status200OK);

        admin.MapGet("/connections", GetConnectionsAsync)
            .Produces<List<WebhookConnectionVM>>(StatusCodes.Status200OK);

        admin.MapGet("/connections/{id:guid}", GetConnectionAsync)
            .Produces<WebhookConnectionVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        admin.MapPost("/connections", CreateAsync)
            .Produces<WebhookConnectionVM>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        admin.MapPut("/connections/{id:guid}", UpdateAsync)
            .Produces<WebhookConnectionVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        admin.MapPost("/connections/{id:guid}/secret", RegenerateSecretAsync)
            .Produces<MediaWebhookSecretVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        admin.MapDelete("/connections/{id:guid}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        admin.MapGet("/deliveries", GetDeliveries)
            .Produces<IReadOnlyList<MediaWebhookDelivery>>(StatusCodes.Status200OK);

        admin.MapPost("/preview", PreviewAsync)
            .Produces<WebhookPreviewVM>(StatusCodes.Status200OK);

        admin.MapGet("/request-servers", GetRequestServersAsync)
            .Produces<List<WebhookRequestServerVM>>(StatusCodes.Status200OK);

        admin.MapGet("/request-servers/{id:guid}/mappings", SuggestMappingsAsync)
            .Produces<WebhookMappingSuggestionVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> ReceiveAsync(string connectionId, HttpRequest request, IMediaWebhookManager manager, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            buffer.Write(chunk, 0, read);
        }

        var body = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        var receipt = await manager.ReceiveAsync(connectionId, body, PresentedSecrets(request));

        return receipt.Status switch
        {
            MediaWebhookStatus.Accepted => Results.Accepted(value: new { message = receipt.Message }),
            MediaWebhookStatus.Unauthorized => Results.Json(new { error = receipt.Message }, statusCode: StatusCodes.Status401Unauthorized),
            MediaWebhookStatus.BadRequest => Results.BadRequest(new { error = receipt.Message }),
            _ => Results.NotFound()
        };
    }

    private static IReadOnlyList<string> PresentedSecrets(HttpRequest request)
    {
        var secrets = new List<string>();

        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authorization[6..].Trim()));
                var colon = decoded.IndexOf(':');
                if (colon >= 0)
                {
                    secrets.Add(decoded[..colon]);
                    secrets.Add(decoded[(colon + 1)..]);
                }
                else
                {
                    secrets.Add(decoded);
                }
            }
            catch (FormatException)
            {
            }
        }

        if (request.Headers.TryGetValue("X-Api-Key", out var key))
        {
            secrets.Add(key.ToString());
        }

        if (request.Query.TryGetValue("token", out var token))
        {
            secrets.Add(token.ToString());
        }

        return secrets.Where(s => s.Length > 0).ToList();
    }

    private static IResult GetProviders(IMediaWebhookManager manager) =>
        Results.Ok(manager.GetProviders());

    private static async Task<IResult> GetConnectionsAsync(IMediaWebhookManager manager) =>
        Results.Ok(await manager.GetConnectionsAsync());

    private static async Task<IResult> GetConnectionAsync(Guid id, IMediaWebhookManager manager)
    {
        var connection = await manager.GetConnectionAsync(id);
        return connection == null ? Results.NotFound() : Results.Ok(connection);
    }

    private static async Task<IResult> CreateAsync([FromBody] SaveWebhookConnectionRequest request, IMediaWebhookManager manager)
    {
        var result = await manager.CreateAsync(request);
        return result.Succeeded
            ? Results.Created($"/api/admin/webhooks/connections/{result.Value!.Id}", result.Value)
            : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] SaveWebhookConnectionRequest request, IMediaWebhookManager manager)
    {
        var result = await manager.UpdateAsync(id, request);
        if (result.IsMissing) return Results.NotFound();
        return result.Succeeded ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> RegenerateSecretAsync(Guid id, IMediaWebhookManager manager)
    {
        var secret = await manager.RegenerateSecretAsync(id);
        return secret == null ? Results.NotFound() : Results.Ok(secret);
    }

    private static async Task<IResult> DeleteAsync(Guid id, IMediaWebhookManager manager) =>
        await manager.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();

    private static IResult GetDeliveries([FromQuery] int? limit, [FromQuery] Guid? connectionId, IMediaWebhookManager manager) =>
        Results.Ok(manager.GetDeliveries(limit ?? 100, connectionId));

    private static async Task<IResult> PreviewAsync([FromBody] WebhookPreviewRequest request, IMediaWebhookManager manager) =>
        Results.Ok(await manager.PreviewAsync(request));

    private static async Task<IResult> GetRequestServersAsync(IMediaWebhookManager manager) =>
        Results.Ok(await manager.GetRequestServersAsync());

    private static async Task<IResult> SuggestMappingsAsync(Guid id, IMediaWebhookManager manager)
    {
        var suggestion = await manager.SuggestMappingsAsync(id);
        return suggestion == null ? Results.NotFound() : Results.Ok(suggestion);
    }
}
