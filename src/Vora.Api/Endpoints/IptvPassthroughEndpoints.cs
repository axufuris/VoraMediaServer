using System.Buffers;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Vora.Api.Extensions;
using Vora.Application.Iptv;
using Vora.Application.Iptv.ViewModels;

namespace Vora.Api.Endpoints;

public class StartPassthroughRequestDto
{
    public Guid ChannelId { get; set; }
}

public static class IptvPassthroughEndpoints
{
    public static IEndpointRouteBuilder MapIptvPassthroughEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/iptv/passthrough").WithTags("IPTV Passthrough");

        group.MapPost("/start", StartAsync).RequireAuthorization()
            .Produces<PassthroughStartVM>(StatusCodes.Status200OK);
        group.MapGet("/playlist.m3u8", GetPlaylistAsync);
        group.MapGet("/segment", GetSegmentAsync);
        group.MapGet("/audio", GetAudioAsync);

        return routes;
    }

    private static async Task<IResult> StartAsync(
        [FromBody] StartPassthroughRequestDto request,
        ClaimsPrincipal user,
        IIptvPassthroughService service)
    {
        var accountId = user.GetAccountId();
        if (accountId == null) return Results.Unauthorized();

        try
        {
            var result = await service.StartPassthroughAsync(request.ChannelId, accountId.Value);
            return Results.Ok(new PassthroughStartVM { Url = result.Url, StreamType = result.StreamType });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (TunerLimitReachedException)
        {
            return Results.Conflict(new { error = "All available tuners for this playlist are in use." });
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> GetPlaylistAsync(
        [FromQuery] string t,
        IIptvPassthroughService service)
    {
        var result = await service.GetRewrittenPlaylistAsync(t);
        if (result == null) return Results.NotFound();
        return Results.Text(result.Content, result.ContentType);
    }

    private static async Task<IResult> GetSegmentAsync(
        [FromQuery] string t,
        IIptvPassthroughService service,
        HttpContext context,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("IptvPassthroughEndpoints.Segment");

        HttpResponseMessage? upstream;
        try
        {
            upstream = await service.FetchSegmentAsync(t, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Segment fetch cancelled by client before upstream response.");
            return Results.Empty;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Segment fetch failed against upstream.");
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        if (upstream == null) return Results.NotFound();

        await PipeUpstreamAsync(upstream, context, logger);
        return Results.Empty;
    }

    private static async Task<IResult> GetAudioAsync(
        [FromQuery] string t,
        IIptvPassthroughService service,
        HttpContext context,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("IptvPassthroughEndpoints.Audio");

        HttpResponseMessage? upstream;
        try
        {
            upstream = await service.FetchAudioStreamAsync(t, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Audio fetch cancelled by client before upstream response.");
            return Results.Empty;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Audio fetch failed against upstream.");
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        if (upstream == null) return Results.NotFound();

        await PipeAudioStreamAsync(upstream, context, logger);
        return Results.Empty;
    }

    private static async Task PipeUpstreamAsync(HttpResponseMessage upstream, HttpContext context, ILogger logger)
    {
        try
        {
            context.Response.StatusCode = (int)upstream.StatusCode;

            if (upstream.Content.Headers.ContentType?.MediaType is { } ct)
            {
                context.Response.ContentType = ct;
            }

            if (upstream.Content.Headers.ContentLength is { } len)
            {
                context.Response.ContentLength = len;
            }

            await using var sourceStream = await upstream.Content.ReadAsStreamAsync(context.RequestAborted);
            await sourceStream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Segment passthrough cancelled by client.");
        }
        catch (IOException ex) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Segment passthrough connection closed by client.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Segment passthrough failed unexpectedly.");
        }
        finally
        {
            upstream.Dispose();
        }
    }

    private static async Task PipeAudioStreamAsync(HttpResponseMessage upstream, HttpContext context, ILogger logger)
    {
        var stopwatch = Stopwatch.StartNew();
        long bytesPiped = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            context.Response.StatusCode = (int)upstream.StatusCode;

            if (upstream.Content.Headers.ContentType?.MediaType is { } ct)
            {
                context.Response.ContentType = ct;
            }

            context.Response.Headers["Cache-Control"] = "no-store";

            await using var sourceStream = await upstream.Content.ReadAsStreamAsync(context.RequestAborted);

            int read;
            while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), context.RequestAborted)) > 0)
            {
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                bytesPiped += read;
            }

            logger.LogInformation("Audio passthrough finished cleanly after {Elapsed}ms, {Bytes} bytes piped.", stopwatch.ElapsedMilliseconds, bytesPiped);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Audio passthrough cancelled by client after {Elapsed}ms, {Bytes} bytes piped.", stopwatch.ElapsedMilliseconds, bytesPiped);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audio passthrough failed after {Elapsed}ms, {Bytes} bytes piped.", stopwatch.ElapsedMilliseconds, bytesPiped);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            upstream.Dispose();
        }
    }
}
