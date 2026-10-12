using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Vora.Application.Media;
using Vora.Infrastructure.Processes;

namespace Vora.Infrastructure.Analysis;

public sealed record VideoStreamRoles(IReadOnlyList<int> Pictures, IReadOnlyList<int> CoverImages)
{
    public static readonly VideoStreamRoles None = new([], []);

    public bool IsCoverImage(int streamIndex) => CoverImages.Contains(streamIndex);

    public IReadOnlyList<string> IgnoreCoverImagesArguments =>
        Pictures.Count == 0
            ? []
            : CoverImages.SelectMany(index => new[] { string.Create(CultureInfo.InvariantCulture, $"-discard:{index}"), "all" }).ToList();
}

public static class CoverImageStreams
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(2);

    public static VideoStreamRoles Read(JsonElement streams)
    {
        if (streams.ValueKind != JsonValueKind.Array) return VideoStreamRoles.None;

        var video = new List<(int Index, bool IsAttachedPicture, bool IsStillImage)>();
        foreach (var stream in streams.EnumerateArray())
        {
            if (!stream.TryGetProperty("codec_type", out var type) || type.GetString() != "video") continue;

            var index = stream.TryGetProperty("index", out var indexProperty) ? indexProperty.GetInt32() : 0;
            var codec = stream.TryGetProperty("codec_name", out var codecProperty) ? codecProperty.GetString() : null;
            video.Add((index, IsAttachedPicture(stream), CoverImageTracks.IsStillImageCodec(codec)));
        }

        var hasMovingPicture = video.Any(v => !v.IsAttachedPicture && !v.IsStillImage);
        var covers = video
            .Where(v => v.IsAttachedPicture || (hasMovingPicture && v.IsStillImage))
            .Select(v => v.Index)
            .ToList();

        return new VideoStreamRoles(video.Select(v => v.Index).Except(covers).ToList(), covers);
    }

    public static async Task<IReadOnlyList<string>> ProbeIgnoreArgumentsAsync(string filePath, ILogger logger, CancellationToken cancellationToken = default)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        processInfo.ArgumentList.Add("-v");
        processInfo.ArgumentList.Add("quiet");
        processInfo.ArgumentList.Add("-print_format");
        processInfo.ArgumentList.Add("json");
        processInfo.ArgumentList.Add("-show_entries");
        processInfo.ArgumentList.Add("stream=index,codec_type,codec_name:stream_disposition=attached_pic");
        processInfo.ArgumentList.Add(filePath);

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return [];

            var json = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitWithTimeoutAsync(ProbeTimeout, logger, cancellationToken);
            if (string.IsNullOrWhiteSpace(json)) return [];

            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("streams", out var streams)
                ? Read(streams).IgnoreCoverImagesArguments
                : [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not look for cover images in {FilePath}; ffmpeg will choose the video stream itself.", filePath);
            return [];
        }
    }

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition)
        && disposition.TryGetProperty("attached_pic", out var attached)
        && attached.ValueKind == JsonValueKind.Number
        && attached.GetInt32() == 1;
}
