using Vora.Api.Extensions;
using Vora.Application.Iptv;
using Vora.Application.Settings;

namespace Vora.Api.Endpoints;

public static class DvrPlaybackEndpoints
{
    public static RouteGroupBuilder MapDvrPlaybackEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/streaming").WithTags("DVR Playback");

        group.MapPost("/dvr/play/{sessionId:guid}", GetPlaybackUrlAsync).RequireAuthorization().RequireFeature(FeatureGate.Dvr);
        group.MapGet("/dvr/file/{sessionId:guid}", ServeRecordingFileAsync);
        group.MapGet("/hls/timeshift/{profileId:guid}/{sessionId}/{fileName}", ServeTimeshiftHlsAsync);

        return group;
    }

    private static async Task<IResult> GetPlaybackUrlAsync(Guid sessionId, IIptvRepository iptvRepo)
    {
        var session = await iptvRepo.GetSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrWhiteSpace(session.OutputFilePath) || !File.Exists(session.OutputFilePath))
        {
            return Results.NotFound("Recording not found or file is inaccessible.");
        }

        return Results.Ok(new { url = $"/api/streaming/dvr/file/{sessionId}" });
    }

    private static async Task<IResult> ServeRecordingFileAsync(Guid sessionId, IIptvRepository iptvRepo)
    {
        var session = await iptvRepo.GetSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrWhiteSpace(session.OutputFilePath) || !File.Exists(session.OutputFilePath))
        {
            return Results.NotFound();
        }

        return Results.File(session.OutputFilePath, "video/mp4", enableRangeProcessing: true);
    }

    private static async Task<IResult> ServeTimeshiftHlsAsync(
        Guid profileId,
        string sessionId,
        string fileName,
        ISystemSettingsRepository settingsRepo,
        HttpContext context)
    {
        var settings = await settingsRepo.GetSettingsAsync();
        var tempDir = string.IsNullOrWhiteSpace(settings.TranscoderTempDirectory) ? "/transcode" : settings.TranscoderTempDirectory;
        var path = Path.Combine(tempDir, "timeshift", profileId.ToString(), sessionId, fileName);

        if (!File.Exists(path))
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
