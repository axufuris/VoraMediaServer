using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    DeviceCapsDto? Capabilities,
    Guid? MediaPartId = null);

public record StartExtraStreamRequest(Guid ExtraId, double StartPosition, DeviceCapsDto? Capabilities);

public record StreamPingRequest(double CurrentPosition, double Duration, bool IsPaused);

public record StreamDecisionResponse(
    Guid MediaPartId,
    Guid VideoTrackId,
    Guid AudioTrackId,
    Guid? SubtitleTrackId,
    string Strategy,
    string VideoStrategy,
    string AudioStrategy,
    string SubtitleStrategy,
    string VideoCodec,
    string AudioCodec,
    string Container,
    int TargetAudioChannels,
    int BandwidthKbps,
    string OutputResolution,
    string OutputHdrType,
    string Quality);

public static class StreamingEndpoints
{
    private const string PlayTokenScope = "play";
    private const string HlsTokenScope = "hls";
    private static readonly TimeSpan HlsTokenTtl = TimeSpan.FromHours(4);

    public static RouteGroupBuilder MapStreamingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/streaming").WithTags("Streaming");

        group.MapPost("/start", StartSessionAsync)
            .WithName("StartStream")
            .Produces<StartStreamResponse>(StatusCodes.Status200OK)
            .RequireAuthorization();
        group.MapPost("/decision", PreviewDecisionAsync)
            .WithName("PreviewStreamDecision")
            .Produces<StreamDecisionResponse>(StatusCodes.Status200OK)
            .RequireAuthorization();
        group.MapPost("/start-extra", StartExtraSessionAsync)
            .WithName("StartExtraStream")
            .Produces<StartStreamResponse>(StatusCodes.Status200OK)
            .RequireAuthorization();
        group.MapPut("/sessions/{sessionId:guid}/ping", PingSessionAsync)
            .WithName("PingStream")
            .RequireAuthorization();
        group.MapDelete("/sessions/{sessionId:guid}", StopSessionAsync)
            .WithName("StopStream")
            .RequireAuthorization();

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
                req.Capabilities,
                req.MediaPartId);

            return Results.Ok(new StartStreamResponse
            {
                SessionId = result.Session.Id,
                StreamUrl = result.StreamUrl,
                VideoTrackId = result.Session.VideoTrackId,
                AudioTrackId = result.Session.AudioTrackId,
                SubtitleTrackId = result.Session.SubtitleTrackId,
                Strategy = result.Session.Strategy,
                VideoStrategy = result.Session.VideoStrategy,
                AudioStrategy = result.Session.AudioStrategy,
                SubtitleStrategy = result.Session.SubtitleStrategy,
                VideoCodec = result.Session.VideoCodec,
                AudioCodec = result.Session.AudioCodec,
                Container = result.Session.Container,
                BandwidthKbps = result.Session.BandwidthKbps,
                TargetAudioChannels = result.Session.TargetAudioChannels,
                OutputResolution = result.Session.OutputResolution,
                OutputHdrType = result.Session.OutputHdrType,
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static async Task<IResult> PreviewDecisionAsync(
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

            var d = await streamManager.PreviewDecisionAsync(
                req.MediaId,
                deviceId,
                accountId,
                profileId,
                req.VideoTrackId,
                req.AudioTrackId,
                req.SubtitleTrackId,
                req.Capabilities,
                req.MediaPartId);

            return Results.Ok(new StreamDecisionResponse(
                d.SelectedMediaPartId,
                d.SelectedVideoTrackId,
                d.SelectedAudioTrackId,
                d.SelectedSubtitleTrackId,
                d.Strategy.ToString(),
                d.VideoStrategy,
                d.AudioStrategy,
                d.SubtitleStrategy,
                d.TargetVideoCodec,
                d.TargetAudioCodec,
                d.TargetContainer,
                d.TargetAudioChannels,
                d.BandwidthKbps,
                d.OutputResolution,
                d.OutputHdrType,
                d.Quality));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static async Task<IResult> StartExtraSessionAsync(
        HttpContext context,
        ClaimsPrincipal user,
        IStreamManager streamManager,
        [FromBody] StartExtraStreamRequest req)
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

            var result = await streamManager.StartExtraSessionAsync(req.ExtraId, deviceId, accountId, profileId, req.StartPosition, req.Capabilities);

            return Results.Ok(new StartStreamResponse
            {
                SessionId = result.Session.Id,
                StreamUrl = result.StreamUrl,
                VideoTrackId = result.Session.VideoTrackId,
                AudioTrackId = result.Session.AudioTrackId,
                SubtitleTrackId = result.Session.SubtitleTrackId,
                Strategy = result.Session.Strategy,
                VideoStrategy = result.Session.VideoStrategy,
                AudioStrategy = result.Session.AudioStrategy,
                SubtitleStrategy = result.Session.SubtitleStrategy,
                VideoCodec = result.Session.VideoCodec,
                AudioCodec = result.Session.AudioCodec,
                Container = result.Session.Container,
                BandwidthKbps = result.Session.BandwidthKbps,
                TargetAudioChannels = result.Session.TargetAudioChannels,
                OutputResolution = result.Session.OutputResolution,
                OutputHdrType = result.Session.OutputHdrType,
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

    private static async Task<IResult> StopSessionAsync(
        Guid sessionId,
        IStreamManager streamManager,
        IStreamRepository streamRepo,
        ITranscodeService transcodeService)
    {
        // Kill the FFmpeg transcode for this session's media before marking
        // the session ended. Without this, the user backing out of the
        // player leaves the encoder running for the full source duration,
        // piling up .ts segments in the transcode dir.
        var session = await streamRepo.GetSessionAsync(sessionId);
        if (session != null)
        {
            await transcodeService.StopTranscodeSessionAsync(session.ExtraId ?? session.MediaItemId);
        }
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
        IStreamingTokenSigner signer,
        ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger("Vora.Api.Streaming.Play");

        var tokenPresent = !string.IsNullOrEmpty(t);
        string payload = string.Empty;
        var verified = tokenPresent && signer.TryVerify(t!, PlayTokenScope, out payload);
        var payloadMatch = verified && payload == sessionId.ToString();
        if (!verified || !payloadMatch)
        {
            log.LogWarning("Play 404 (token verify failed) sessionId={SessionId} hasToken={HasToken} verified={Verified} payloadMatch={PayloadMatch}",
                sessionId, tokenPresent, verified, payloadMatch);
            return Results.NotFound();
        }

        var session = await streamRepo.GetSessionAsync(sessionId);
        if (session == null)
        {
            log.LogWarning("Play 404 (session lookup returned null) sessionId={SessionId}", sessionId);
            return Results.NotFound("Session not found.");
        }

        var filePath = await streamManager.GetPlayableFilePathAsync(sessionId);
        if (filePath == null)
        {
            log.LogWarning("Play 404 (file path null) sessionId={SessionId} mediaItemId={MediaItemId} strategy={Strategy}",
                sessionId, session.MediaItemId, session.Strategy);
            return Results.NotFound("Media file not found or inaccessible.");
        }

        if (session.Strategy == "DirectPlay")
        {
            return Results.File(filePath, enableRangeProcessing: true);
        }

        // Load the picked part's tracks so we can resolve the session's
        // VideoTrackId / AudioTrackId / SubtitleTrackId Guids back into the
        // stream indexes FFmpeg's `-map` flag understands. Without this,
        // FFmpeg auto-selects the file's default-flagged streams regardless
        // of what the user picked in the Quality panel.
        var part = await streamRepo.GetMediaPartForSessionAsync(sessionId);
        var pickedVideo = part?.VideoTracks.FirstOrDefault(t => t.Id == session.VideoTrackId);
        var pickedAudio = part?.AudioTracks.FirstOrDefault(t => t.Id == session.AudioTrackId);
        var pickedSubtitleIdx = session.SubtitleTrackId.HasValue
            ? part?.SubtitleTracks.FirstOrDefault(t => t.Id == session.SubtitleTrackId.Value)?.StreamIndex
            : null;

        var decision = BuildPlaybackDecision(session);
        decision.SelectedVideoStreamIndex = pickedVideo?.StreamIndex;
        decision.SelectedAudioStreamIndex = pickedAudio?.StreamIndex;
        decision.SelectedSubtitleStreamIndex = pickedSubtitleIdx;
        decision.SourceVideoCodec = pickedVideo?.Codec;
        decision.SourceAudioCodec = pickedAudio?.Codec;
        decision.SourceDurationSeconds = part?.Duration?.TotalSeconds ?? 0.0;
        decision.StartPositionSeconds = session.StartPosition;
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
            log.LogError(ex, "Play 400 (transcode start threw) sessionId={SessionId} filePath={FilePath}", sessionId, filePath);
            return Results.BadRequest(ex.Message);
        }
    }

    private static async Task<IResult> ServeHlsChunkAsync(
        string token,
        string fileName,
        ISystemSettingsRepository settingsRepo,
        IStreamingTokenSigner signer,
        ITranscodeService transcodeService,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var log = loggerFactory.CreateLogger("Vora.Api.Streaming.HlsChunk");

        try
        {
            return await ServeHlsChunkInnerAsync(token, fileName, settingsRepo, signer, transcodeService, log, ct);
        }
        catch (OperationCanceledException)
        {
            // Player aborted this chunk request (typically because it
            // seeked elsewhere and gave up waiting for the old segment).
            // Don't surface this as a 500 — it's expected and harmless.
            return Results.NoContent();
        }
    }

    private static async Task<IResult> ServeHlsChunkInnerAsync(
        string token,
        string fileName,
        ISystemSettingsRepository settingsRepo,
        IStreamingTokenSigner signer,
        ITranscodeService transcodeService,
        ILogger log,
        CancellationToken ct)
    {

        string payload = string.Empty;
        var verified = signer.TryVerify(token, HlsTokenScope, out payload);
        if (!verified || string.IsNullOrEmpty(payload))
        {
            log.LogWarning("HlsChunk 404 (token verify failed) fileName={FileName} verified={Verified} hasPayload={HasPayload}",
                fileName, verified, !string.IsNullOrEmpty(payload));
            return Results.NotFound();
        }

        if (!fileName.StartsWith(payload, StringComparison.Ordinal))
        {
            log.LogWarning("HlsChunk 404 (fileName prefix mismatch) fileName={FileName} expectedPrefix={Prefix}",
                fileName, payload);
            return Results.NotFound();
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".m3u8" or ".ts" or ".m4s" or ".mp4" or ".vtt"))
        {
            log.LogWarning("HlsChunk 404 (unsupported extension) fileName={FileName} ext={Ext}", fileName, ext);
            return Results.NotFound();
        }

        var settings = await settingsRepo.GetSettingsAsync();
        var tempDir = ResolveTempDirectory(settings);
        var path = SafePathResolver.ResolveContainedFilePath(tempDir, fileName);

        if (path == null)
        {
            log.LogWarning("HlsChunk 404 (path resolution failed) fileName={FileName} tempDir={TempDir}", fileName, tempDir);
            return Results.NotFound();
        }

        // For .ts requests, route through the transcode service so the
        // segment is sealed (FFmpeg has finished writing it) before we
        // try to serve it. This handles three cases:
        //   (a) Player is reading ahead of FFmpeg's current encoding
        //       position — wait briefly for the segment to finish.
        //   (b) Player seeked far forward — kill FFmpeg and relaunch from
        //       the new position (Plex-style segment-on-demand).
        //   (c) File exists on disk but FFmpeg is mid-write — wait for
        //       seal before serving, otherwise Content-Length is wrong
        //       (the "too many bytes written" exception) and the player
        //       stutters on a truncated segment.
        Guid? servedMediaItemId = null;
        int servedSegmentIndex = -1;
        if (ext == ".ts")
        {
            if (TryParseSegmentRequest(fileName, out var mediaItemId, out var segmentIndex))
            {
                var ready = await transcodeService.EnsureSegmentAvailableAsync(mediaItemId, segmentIndex, ct);
                if (!ready)
                {
                    log.LogWarning("HlsChunk 404 (segment not sealed after wait) fileName={FileName} mediaItemId={MediaItemId} segmentIndex={SegmentIndex}",
                        fileName, mediaItemId, segmentIndex);
                    return Results.NotFound();
                }
                servedMediaItemId = mediaItemId;
                servedSegmentIndex = segmentIndex;
            }
        }

        if (!File.Exists(path))
        {
            log.LogWarning("HlsChunk 404 (file not found after ensure) fileName={FileName} tempDir={TempDir} resolvedPath={Path}",
                fileName, tempDir, path);
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

        // Tell the transcode service that this segment actually got
        // served. EnsureSegmentAvailableAsync uses this to distinguish
        // between the player's initial pre-seek probe (no segments
        // served yet → suppress backward-segment requests) and a real
        // user seek after playback has started (allow seek-restart).
        if (servedMediaItemId.HasValue)
        {
            transcodeService.NotifySegmentServed(servedMediaItemId.Value, servedSegmentIndex);
        }

        return Results.File(path, contentType, enableRangeProcessing: true);
    }

    private static string ComputeHlsPrefix(string playlistFileName)
    {
        var dot = playlistFileName.LastIndexOf('.');
        return dot > 0 ? playlistFileName[..dot] : playlistFileName;
    }

    private static bool TryParseSegmentRequest(string fileName, out Guid mediaItemId, out int segmentIndex)
    {
        mediaItemId = Guid.Empty;
        segmentIndex = -1;
        var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
        var underscore = nameNoExt.LastIndexOf('_');
        if (underscore <= 0 || underscore >= nameNoExt.Length - 1) return false;
        if (!Guid.TryParse(nameNoExt[..underscore], out mediaItemId)) return false;
        if (!int.TryParse(nameNoExt[(underscore + 1)..], out segmentIndex)) return false;
        return true;
    }

    private static PlaybackDecisionVM BuildPlaybackDecision(Vora.Domain.Entities.Streaming.StreamSession session) => new()
    {
        MediaItemId = session.ExtraId ?? session.MediaItemId,
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
        TranscodeReason = session.DecisionLog ?? string.Empty,
        SourceHdrType = session.HdrType,
        OutputHeight = ResolveOutputHeight(session),
    };

    private static int ResolveOutputHeight(Vora.Domain.Entities.Streaming.StreamSession session)
    {
        if (string.IsNullOrWhiteSpace(session.OutputResolution) || string.IsNullOrWhiteSpace(session.Resolution))
        {
            return 0;
        }

        var decidedHeight = BestPathDecisionManager.ParseHeightFromResolution(session.OutputResolution);
        var sourceHeight = BestPathDecisionManager.ParseHeightFromResolution(session.Resolution);
        return decidedHeight > 0 && decidedHeight < sourceHeight ? decidedHeight : 0;
    }

    private static string ResolveTempDirectory(Vora.Domain.Entities.Settings.ServerSetting settings) =>
        string.IsNullOrWhiteSpace(settings.TranscoderTempDirectory) ? "/transcode" : settings.TranscoderTempDirectory;
}
