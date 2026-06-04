using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.FileSystem;
using Vora.Application.Iptv;
using Vora.Application.Iptv.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Streaming;

namespace Vora.Api.Endpoints;

public static class DvrPlaybackEndpoints
{
    private const string DvrTokenScope = "dvr";
    private const string TimeshiftTokenScope = "timeshift";
    private static readonly TimeSpan DvrTokenTtl = TimeSpan.FromHours(2);
    private static readonly TimeSpan TimeshiftTokenTtl = TimeSpan.FromHours(4);

    public static RouteGroupBuilder MapDvrPlaybackEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/streaming").WithTags("DVR Playback");

        group.MapPost("/dvr/play/{sessionId:guid}", GetPlaybackUrlAsync)
            .RequireAuthorization()
            .RequireFeature(FeatureGate.Dvr)
            .WithName("PlayDvrSession")
            .Produces<DvrPlaybackUrlResponse>(StatusCodes.Status200OK);
        group.MapGet("/dvr/file/{sessionId:guid}", ServeRecordingFileAsync);
        group.MapGet("/hls/timeshift/{token}/{profileId:guid}/{sessionId}/{fileName}", ServeTimeshiftHlsAsync);

        return group;
    }

    private static async Task<IResult> GetPlaybackUrlAsync(Guid sessionId, IIptvRepository iptvRepo, IStreamingTokenSigner signer)
    {
        var session = await iptvRepo.GetSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrWhiteSpace(session.OutputFilePath) || !File.Exists(session.OutputFilePath))
        {
            return Results.NotFound("Recording not found or file is inaccessible.");
        }

        var token = signer.Sign(DvrTokenScope, sessionId.ToString(), DvrTokenTtl);
        return Results.Ok(new DvrPlaybackUrlResponse
        {
            Url = $"/api/streaming/dvr/file/{sessionId}?t={token}",
        });
    }

    private static async Task<IResult> ServeRecordingFileAsync(
        Guid sessionId,
        [FromQuery] string? t,
        IIptvRepository iptvRepo,
        IStreamingTokenSigner signer)
    {
        if (string.IsNullOrEmpty(t) || !signer.TryVerify(t, DvrTokenScope, out var payload) || payload != sessionId.ToString())
        {
            return Results.NotFound();
        }

        var session = await iptvRepo.GetSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrWhiteSpace(session.OutputFilePath) || !File.Exists(session.OutputFilePath))
        {
            return Results.NotFound();
        }

        return Results.File(session.OutputFilePath, "video/mp4", enableRangeProcessing: true);
    }

    private static async Task<IResult> ServeTimeshiftHlsAsync(
        string token,
        Guid profileId,
        string sessionId,
        string fileName,
        ISystemSettingsRepository settingsRepo,
        IStreamingTokenSigner signer,
        HttpContext context)
    {
        if (!signer.TryVerify(token, TimeshiftTokenScope, out var payload) || payload != $"{profileId}:{sessionId}")
        {
            return Results.NotFound();
        }

        var settings = await settingsRepo.GetSettingsAsync();
        var tempDir = string.IsNullOrWhiteSpace(settings.TranscoderTempDirectory) ? "/transcode" : settings.TranscoderTempDirectory;
        var path = SafePathResolver.ResolveContainedSubPath(tempDir, "timeshift", profileId.ToString(), sessionId, fileName);

        if (path == null || !File.Exists(path))
        {
            return Results.NotFound();
        }

        var contentType = fileName.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : "video/MP2T";

        if (fileName.EndsWith(".m3u8"))
        {
            context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            context.Response.Headers.Append("Pragma", "no-cache");
            context.Response.Headers.Append("Expires", "0");
        }

        return Results.File(path, contentType, enableRangeProcessing: true);
    }
}
