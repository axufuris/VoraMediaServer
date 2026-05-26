using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Vora.Application.Streaming;

namespace Vora.Infrastructure.Transcoding;

public class FFmpegAudioTranscodeService : IAudioTranscodeService
{
    private readonly ILogger<FFmpegAudioTranscodeService> _logger;

    public FFmpegAudioTranscodeService(ILogger<FFmpegAudioTranscodeService> logger)
    {
        _logger = logger;
    }

    public string ResolveContentType(string targetCodec)
    {
        return NormalizeCodec(targetCodec) switch
        {
            "mp3" => "audio/mpeg",
            "aac" => "audio/aac",
            "opus" => "audio/opus",
            _ => "audio/mpeg"
        };
    }

    public async Task WriteTranscodedAudioAsync(string sourceFilePath, int bitrateKbps, string targetCodec, Stream output, CancellationToken cancellationToken)
    {
        var codec = NormalizeCodec(targetCodec);
        var (encoder, containerFormat) = ResolveEncoderAndFormat(codec);

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(sourceFilePath);
        psi.ArgumentList.Add("-vn");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add(encoder);
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add($"{bitrateKbps}k");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(containerFormat);
        psi.ArgumentList.Add("pipe:1");

        using var process = Process.Start(psi);
        if (process == null)
        {
            _logger.LogWarning("Failed to start FFmpeg for audio transcode of {SourceFilePath}.", sourceFilePath);
            return;
        }

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(output, 81920, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to terminate FFmpeg audio transcode for {SourceFilePath}.", sourceFilePath);
            }
        }
    }

    private static string NormalizeCodec(string targetCodec)
    {
        if (string.IsNullOrWhiteSpace(targetCodec)) return "mp3";
        return targetCodec.Trim().ToLowerInvariant();
    }

    private static (string encoder, string containerFormat) ResolveEncoderAndFormat(string codec)
    {
        return codec switch
        {
            "aac" => ("aac", "adts"),
            "opus" => ("libopus", "opus"),
            _ => ("libmp3lame", "mp3")
        };
    }
}
