using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.FileSystem;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;

namespace Vora.Api.Endpoints;

public record StartStreamRequest(
    Guid MediaId,
    string DeviceId,
    double StartPosition,
    Guid? VideoTrackId,
    Guid? AudioTrackId,
    Guid? SubtitleTrackId,
    DeviceCapsDto? Capabilities);

public record StreamPingRequest(double CurrentPosition, double Duration, bool IsPaused);

public static class StreamingEndpoints
{
    private const string PlayTokenScope = "play";
    private const string HlsTokenScope = "hls";
    private static readonly TimeSpan HlsTokenTtl = TimeSpan.FromHours(4);

    public static RouteGroupBuilder MapStreamingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/streaming").WithTags("Streaming");

        group.MapPost("/start", StartSessionAsync).RequireAuthorization();
        group.MapPut("/sessions/{sessionId:guid}/ping", PingSessionAsync).RequireAuthorization();
        group.MapDelete("/sessions/{sessionId:guid}", StopSessionAsync).RequireAuthorization();

        group.MapGet("/play/{sessionId:guid}", PlaySessionAsync);
        group.MapGet("/hls/s/{token}/{fileName}", ServeHlsChunkAsync);

        return group;
    }

    private static async Task<IResult> StartSessionAsync(
        HttpContext context,
        ClaimsPrincipal user,
        IStreamManager streamManager,
        [FromBody] StartStreamRequest req)
    {
        try
        {
            var accountId = user.GetAccountId() ?? Guid.Empty;
            var profileId = user.GetProfileId();

            var deviceId = context.Request.Headers["X-Vora-Device-Id"].FirstOrDefault();
            if (string.IsNullOrEmpty(deviceId))
            {
                return Results.BadRequest("Missing X-Vora-Device-Id header");
            }

            var result = await streamManager.StartSessionAsync(
                req.MediaId,
                deviceId,
                accountId,
                profileId,
                req.StartPosition,
                req.VideoTrackId,
                req.AudioTrackId,
                req.SubtitleTrackId,
                req.Capabilities);

            return Results.Ok(new
            {
                SessionId = result.Session.Id,
                result.StreamUrl,
                result.Session.VideoTrackId,
                result.Session.AudioTrackId,
                result.Session.SubtitleTrackId,
                result.Session.Strategy,
                result.Session.VideoStrategy,
                result.Session.AudioStrategy,
                result.Session.SubtitleStrategy,
                result.Session.VideoCodec,
                result.Session.AudioCodec,
                result.Session.Container,
                result.Session.BandwidthKbps,
                result.Session.TargetAudioChannels
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static async Task<IResult> PingSessionAsync(Guid sessionId, IStreamManager streamManager, [FromBody] StreamPingRequest req)
    {
        await streamManager.PingSessionAsync(sessionId, req.CurrentPosition, req.Duration, req.IsPaused);
        return Results.NoContent();
    }

    private static async Task<IResult> StopSessionAsync(Guid sessionId, IStreamManager streamManager)
    {
        await streamManager.StopSessionAsync(sessionId);
        return Results.NoContent();
    }

    private static async Task<IResult> PlaySessionAsync(
        Guid sessionId,
        [FromQuery] string? t,
        IStreamManager streamManager,
        IStreamRepository streamRepo,
        ITranscodeService transcodeService,
        ISystemSettingsRepository settingsRepo,
        IStreamingTokenSigner signer)
    {
        if (string.IsNullOrEmpty(t) || !signer.TryVerify(t, PlayTokenScope, out var payload) || payload != sessionId.ToString())
        {
            return Results.NotFound();
        }

        var session = await streamRepo.GetSessionAsync(sessionId);
        if (session == null)
        {
            return Results.NotFound("Session not found.");
        }

        var filePath = await streamManager.GetPlayableFilePathAsync(sessionId);
        if (filePath == null)
        {
            return Results.NotFound("Media file not found or inaccessible.");
        }

        if (session.Strategy == "DirectPlay")
        {
            return Results.File(filePath, enableRangeProcessing: true);
        }

        var decision = BuildPlaybackDecision(session);
        var settings = await settingsRepo.GetSettingsAsync();
        var tempDir = ResolveTempDirectory(settings);

        try
        {
            var playlistPath = await transcodeService.StartTranscodeSessionAsync(filePath, tempDir, decision, CancellationToken.None);
            var playlistFileName = Path.GetFileName(playlistPath);
            var prefix = ComputeHlsPrefix(playlistFileName);
            var hlsToken = signer.Sign(HlsTokenScope, prefix, HlsTokenTtl);
            return Results.Redirect($"/api/streaming/hls/s/{hlsToken}/{playlistFileName}");
        }
        catch (Exception ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static async Task<IResult> ServeHlsChunkAsync(
        string token,
        string fileName,
        ISystemSettingsRepository settingsRepo,
        IStreamingTokenSigner signer)
    {
        if (!signer.TryVerify(token, HlsTokenScope, out var payload) || string.IsNullOrEmpty(payload))
        {
            return Results.NotFound();
        }

        if (!fileName.StartsWith(payload, StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".m3u8" or ".ts" or ".m4s" or ".mp4" or ".vtt"))
        {
            return Results.NotFound();
        }

        var settings = await settingsRepo.GetSettingsAsync();
        var tempDir = ResolveTempDirectory(settings);
        var path = SafePathResolver.ResolveContainedFilePath(tempDir, fileName);

        if (path == null || !File.Exists(path))
        {
            return Results.NotFound();
        }

        var contentType = ext switch
        {
            ".m3u8" => "application/vnd.apple.mpegurl",
            ".m4s" => "video/iso.segment",
            ".mp4" => "video/mp4",
            ".vtt" => "text/vtt",
            _ => "video/MP2T"
        };
        return Results.File(path, contentType, enableRangeProcessing: true);
    }

    private static string ComputeHlsPrefix(string playlistFileName)
    {
        var dot = playlistFileName.LastIndexOf('.');
        return dot > 0 ? playlistFileName[..dot] : playlistFileName;
    }

    private static PlaybackDecisionVM BuildPlaybackDecision(Vora.Domain.Entities.Streaming.StreamSession session) => new()
    {
        MediaItemId = session.MediaItemId,
        Decision = Enum.TryParse<StreamingState>(session.Strategy, out var s) ? s : StreamingState.Transcode,
        TargetVideoCodec = string.Equals(session.VideoCodec, "hevc", StringComparison.OrdinalIgnoreCase) || string.Equals(session.VideoCodec, "h265", StringComparison.OrdinalIgnoreCase)
            ? VideoCodec.Hevc
            : VideoCodec.H264,
        TargetAudioCodec = string.Equals(session.AudioCodec, "aac", StringComparison.OrdinalIgnoreCase) ? AudioCodec.Aac : AudioCodec.Ac3,
        TargetContainer = session.Container,
        RequiresSubtitleBurnIn = session.IsSubtitleBurnIn,
        VideoStrategy = session.VideoStrategy,
        AudioStrategy = session.AudioStrategy,
        BandwidthKbps = session.BandwidthKbps,
        TargetAudioChannels = session.TargetAudioChannels,
        TranscodeReason = session.DecisionLog ?? string.Empty
    };

    private static string ResolveTempDirectory(Vora.Domain.Entities.Settings.ServerSetting settings) =>
        string.IsNullOrWhiteSpace(settings.TranscoderTempDirectory) ? "/transcode" : settings.TranscoderTempDirectory;
}
