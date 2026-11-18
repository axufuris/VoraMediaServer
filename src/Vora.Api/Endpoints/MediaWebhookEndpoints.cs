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

        receive.MapMethods("/{providerId}", new[] { HttpMethods.Post, HttpMethods.Put }, ReceiveAsync)
            .ExcludeFromDescription();

        var admin = routes.MapGroup("/api/admin/webhooks")
            .WithTags("Media Webhooks (Admin)")
            .RequireAuthorization("AdminOnly");

        admin.MapGet("/", GetProvidersAsync)
            .Produces<List<MediaWebhookProviderVM>>(StatusCodes.Status200OK);

        admin.MapGet("/deliveries", GetDeliveries)
            .Produces<IReadOnlyList<MediaWebhookDelivery>>(StatusCodes.Status200OK);

        admin.MapPost("/{providerId}/secret", GenerateSecretAsync)
            .Produces<MediaWebhookSecretVM>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> ReceiveAsync(string providerId, HttpRequest request, IMediaWebhookManager manager, CancellationToken cancellationToken)
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
        var receipt = await manager.ReceiveAsync(providerId, body, PresentedSecrets(request));

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

    private static async Task<IResult> GetProvidersAsync(IMediaWebhookManager manager) =>
        Results.Ok(await manager.GetProvidersAsync());

    private static IResult GetDeliveries([FromQuery] int? limit, IMediaWebhookManager manager) =>
        Results.Ok(manager.GetDeliveries(limit ?? 100));

    private static async Task<IResult> GenerateSecretAsync(string providerId, IMediaWebhookManager manager)
    {
        var secret = await manager.GenerateSecretAsync(providerId);
        return secret == null ? Results.NotFound() : Results.Ok(secret);
    }
}
