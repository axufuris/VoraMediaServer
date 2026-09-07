using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Vora.Application.Streaming;

namespace Vora.Infrastructure.Transcoding;

public class FFmpegSubtitleExtractionService : ISubtitleExtractionService
{
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<FFmpegSubtitleExtractionService> _logger;

    public FFmpegSubtitleExtractionService(ILogger<FFmpegSubtitleExtractionService> logger)
    {
        _logger = logger;
    }

    public static string WebVttFileName(Guid transcodeKey) => $"{transcodeKey}_subtitles.vtt";

    public async Task<string?> ExtractWebVttAsync(string sourceFilePath, int subtitleStreamIndex, string outputDirectory, Guid transcodeKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath)) return null;
        if (string.IsNullOrWhiteSpace(outputDirectory)) return null;

        Directory.CreateDirectory(outputDirectory);

        var fileName = WebVttFileName(transcodeKey);
        var outputPath = Path.Combine(outputDirectory, fileName);

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourceFilePath);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add($"0:{subtitleStreamIndex}");
        startInfo.ArgumentList.Add("-c:s");
        startInfo.ArgumentList.Add("webvtt");
        startInfo.ArgumentList.Add(outputPath);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ExtractionTimeout);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                _logger.LogWarning("Subtitle extraction timed out after {Timeout}s for {Source} stream {StreamIndex}.",
                    ExtractionTimeout.TotalSeconds, sourceFilePath, subtitleStreamIndex);
                TryDelete(outputPath);
                return null;
            }

            if (process.ExitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                _logger.LogWarning("Subtitle extraction failed for {Source} stream {StreamIndex} (exit {ExitCode}): {Error}",
                    sourceFilePath, subtitleStreamIndex, process.ExitCode, await stderr);
                TryDelete(outputPath);
                return null;
            }

            return fileName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subtitle extraction threw for {Source} stream {StreamIndex}.", sourceFilePath, subtitleStreamIndex);
            TryDelete(outputPath);
            return null;
        }
    }

    public void RemoveWebVtt(string outputDirectory, Guid transcodeKey)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) return;
        TryDelete(Path.Combine(outputDirectory, WebVttFileName(transcodeKey)));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}
