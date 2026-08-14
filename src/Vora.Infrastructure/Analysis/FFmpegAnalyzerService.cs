using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vora.Application.Analysis;
using Vora.Application.Analysis.Results;
using Vora.Infrastructure.Processes;

namespace Vora.Infrastructure.Analysis;

public class FFmpegAnalyzerService : IMediaAnalyzerService
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromHours(2);
    private const int MeanVolumeSampleSeconds = 600;
    private static readonly Regex SilenceStartRegex = new(@"silence_start:\s*(?<Time>-?\d+(\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex SilenceEndRegex = new(@"silence_end:\s*(?<Time>-?\d+(\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex BlackStartRegex = new(@"black_start[:=]\s*(?<Time>\d+(\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex BlackEndRegex = new(@"black_end[:=]\s*(?<Time>\d+(\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex MeanVolumeRegex = new(@"mean_volume:\s*(?<Db>-?\d+(\.\d+)?)\s*dB", RegexOptions.Compiled);

    private readonly ILogger<FFmpegAnalyzerService> _logger;

    public FFmpegAnalyzerService(ILogger<FFmpegAnalyzerService> logger)
    {
        _logger = logger;
    }

    public async Task<MediaAnalysisResult> AnalyzeFileAsync(string filePath)
    {
        var result = new MediaAnalysisResult();
        await RunFFprobeAsync(filePath, result);
        return result;
    }

    public async Task<double?> ProbeMeanVolumeDbAsync(string filePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Probing mean volume for {FilePath}.", filePath);

        var processInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        processInfo.ArgumentList.Add("-i");
        processInfo.ArgumentList.Add(filePath);
        // Only decode the first few minutes: this pass exists solely to pick a
        // noise threshold from the audio's mean level, which a short head sample
        // represents fine — no need to decode the whole file's audio just for that.
        processInfo.ArgumentList.Add("-t");
        processInfo.ArgumentList.Add(MeanVolumeSampleSeconds.ToString(CultureInfo.InvariantCulture));
        processInfo.ArgumentList.Add("-af");
        processInfo.ArgumentList.Add("volumedetect");
        processInfo.ArgumentList.Add("-vn");
        processInfo.ArgumentList.Add("-sn");
        processInfo.ArgumentList.Add("-f");
        processInfo.ArgumentList.Add("null");
        processInfo.ArgumentList.Add("-");

        double? meanDb = null;

        using var process = new Process { StartInfo = processInfo };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var match = MeanVolumeRegex.Match(e.Data);
            if (match.Success && double.TryParse(match.Groups["Db"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var db))
            {
                meanDb = db;
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        await process.WaitForExitWithTimeoutAsync(ProcessTimeout, _logger, cancellationToken);

        return meanDb;
    }

    public async Task<MediaAnalysisResult> AnalyzeSilenceDetectionsAsync(string filePath, SilenceDetectionParameters parameters, CancellationToken cancellationToken = default)
    {
        var result = new MediaAnalysisResult();
        await RunFFmpegSilenceAndBlackDetectionAsync(filePath, parameters, result, cancellationToken);
        return result;
    }

    private static string? GetTagValue(JsonElement tags, string keyToFind)
    {
        if (tags.ValueKind != JsonValueKind.Object) return null;

        string? foundValue = null;

        foreach (var tag in tags.EnumerateObject())
        {
            if (tag.Name.Equals(keyToFind, StringComparison.OrdinalIgnoreCase))
            {
                foundValue = tag.Value.GetString()?.Trim('"');
                break;
            }
        }

        if (foundValue == null)
        {
            foreach (var tag in tags.EnumerateObject())
            {
                if (tag.Name.StartsWith(keyToFind, StringComparison.OrdinalIgnoreCase))
                {
                    foundValue = tag.Value.GetString()?.Trim('"');
                    break;
                }
            }
        }

        if (foundValue == "eng")
            foundValue = "English";

        if (!string.IsNullOrWhiteSpace(foundValue))
        {
            return char.ToUpper(foundValue[0]) + foundValue.Substring(1);
        }

        return foundValue;
    }

    private async Task RunFFprobeAsync(string filePath, MediaAnalysisResult result)
    {
        _logger.LogInformation("Extracting technical metadata via FFprobe for {FilePath}.", filePath);

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
        processInfo.ArgumentList.Add("-show_streams");
        processInfo.ArgumentList.Add("-show_format");
        processInfo.ArgumentList.Add(filePath);

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) throw new InvalidOperationException("Failed to start FFprobe process.");

            string jsonOutput = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitWithTimeoutAsync(ProcessTimeout, _logger);

            if (string.IsNullOrWhiteSpace(jsonOutput)) return;

            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            if (root.TryGetProperty("format", out var formatBlock))
            {
                if (formatBlock.TryGetProperty("bit_rate", out var brProp) && long.TryParse(brProp.GetString(), out var br))
                    result.OverallBitrate = br;

                if (formatBlock.TryGetProperty("size", out var sizeProp) && long.TryParse(sizeProp.GetString(), out var size))
                    result.FileSizeBytes = size;

                if (formatBlock.TryGetProperty("duration", out var durProp) &&
                    double.TryParse(durProp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double durationSeconds))
                {
                    result.Duration = TimeSpan.FromSeconds(durationSeconds);
                }
            }

            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() : "";
                    var index = stream.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;

                    bool isDefault = false;
                    bool isForced = false;
                    if (stream.TryGetProperty("disposition", out var disp))
                    {
                        isDefault = disp.TryGetProperty("default", out var def) && def.GetInt32() == 1;
                        isForced = disp.TryGetProperty("forced", out var f) && f.GetInt32() == 1;
                    }

                    if (codecType == "video")
                    {
                        var videoTrack = new VideoTrackInfo
                        {
                            StreamIndex = index,
                            Codec = stream.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null,
                            Profile = stream.TryGetProperty("profile", out var p) ? p.GetString() : null,
                            IsDefault = isDefault
                        };

                        if (stream.TryGetProperty("bits_per_raw_sample", out var bprs) && int.TryParse(bprs.GetString(), out var bd)) videoTrack.BitDepth = bd;
                        else if (videoTrack.Profile != null && videoTrack.Profile.Contains("10")) videoTrack.BitDepth = 10;
                        else if (stream.TryGetProperty("pix_fmt", out var pixFmtProp))
                        {
                            var pixFmt = pixFmtProp.GetString() ?? "";
                            if (pixFmt.Contains("p10") || pixFmt.Contains("10le")) videoTrack.BitDepth = 10;
                            else if (pixFmt.Contains("p12") || pixFmt.Contains("12le")) videoTrack.BitDepth = 12;
                            else videoTrack.BitDepth = 8;
                        }

                        string? hdrType = null;
                        if (stream.TryGetProperty("color_transfer", out var ctProp))
                        {
                            var colorTransfer = ctProp.GetString();
                            if (colorTransfer == "smpte2084") hdrType = "HDR10";
                            else if (colorTransfer == "arib-std-b67") hdrType = "HLG";
                        }

                        if (stream.TryGetProperty("side_data_list", out var sideDataList) && sideDataList.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var sideData in sideDataList.EnumerateArray())
                            {
                                if (sideData.TryGetProperty("side_data_type", out var sdt) && sdt.GetString() == "DOVI configuration record")
                                {
                                    hdrType = hdrType == "HDR10" ? "DoVi/HDR10" : "DoVi";
                                    break;
                                }
                            }
                        }
                        videoTrack.HdrType = hdrType;

                        if (stream.TryGetProperty("bit_rate", out var vbrProp) && long.TryParse(vbrProp.GetString(), out var vbr))
                        {
                            videoTrack.Bitrate = vbr;
                        }
                        else if (stream.TryGetProperty("tags", out var vTags))
                        {
                            var bpsString = GetTagValue(vTags, "BPS");
                            if (!string.IsNullOrEmpty(bpsString) && long.TryParse(bpsString, out var bps))
                            {
                                videoTrack.Bitrate = bps;
                            }
                        }

                        result.VideoTracks.Add(videoTrack);
                    }
                    else if (codecType == "audio")
                    {
                        var audioTrack = new AudioTrackInfo
                        {
                            StreamIndex = index,
                            Codec = stream.TryGetProperty("codec_name", out var acn) ? acn.GetString()?.ToUpperInvariant() : null,
                            Channels = stream.TryGetProperty("channels", out var ch) ? ch.GetInt32() : (int?)null,
                            IsDefault = isDefault
                        };

                        if (stream.TryGetProperty("tags", out var tags))
                        {
                            audioTrack.Language = GetTagValue(tags, "language");
                            audioTrack.Title = GetTagValue(tags, "title");
                        }

                        result.AudioTracks.Add(audioTrack);
                    }
                    else if (codecType == "subtitle")
                    {
                        var subTrack = new SubtitleTrackInfo
                        {
                            StreamIndex = index,
                            Codec = stream.TryGetProperty("codec_name", out var scn) ? scn.GetString() : null,
                            IsDefault = isDefault,
                            IsForced = isForced
                        };

                        if (stream.TryGetProperty("tags", out var tags))
                        {
                            subTrack.Language = GetTagValue(tags, "language");
                            subTrack.Title = GetTagValue(tags, "title");
                        }

                        result.SubtitleTracks.Add(subTrack);
                    }
                }
            }

            await UpgradeHdr10PlusAsync(filePath, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFprobe analysis failed for {FilePath}.", filePath);
            throw;
        }
    }

    private async Task UpgradeHdr10PlusAsync(string filePath, MediaAnalysisResult result)
    {
        var hdr10Tracks = result.VideoTracks
            .Where(t => t.HdrType == "HDR10" || t.HdrType == "DoVi/HDR10")
            .ToList();

        if (hdr10Tracks.Count == 0) return;
        if (!await HasHdr10PlusMetadataAsync(filePath)) return;

        foreach (var track in hdr10Tracks)
        {
            track.HdrType = track.HdrType == "DoVi/HDR10" ? "DoVi/HDR10Plus" : "HDR10Plus";
        }
    }

    private async Task<bool> HasHdr10PlusMetadataAsync(string filePath)
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
        processInfo.ArgumentList.Add("-select_streams");
        processInfo.ArgumentList.Add("v:0");
        processInfo.ArgumentList.Add("-read_intervals");
        processInfo.ArgumentList.Add("%+#5");
        processInfo.ArgumentList.Add("-show_frames");
        processInfo.ArgumentList.Add("-show_entries");
        processInfo.ArgumentList.Add("frame=side_data_list");
        processInfo.ArgumentList.Add(filePath);

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return false;

            string jsonOutput = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitWithTimeoutAsync(ProcessTimeout, _logger);

            return Hdr10PlusJsonIndicatesDynamicMetadata(jsonOutput);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HDR10+ frame probe failed for {FilePath}; leaving it as HDR10.", filePath);
            return false;
        }
    }

    internal static bool Hdr10PlusJsonIndicatesDynamicMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("frames", out var frames) || frames.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var frame in frames.EnumerateArray())
            {
                if (!frame.TryGetProperty("side_data_list", out var list) || list.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var sideData in list.EnumerateArray())
                {
                    if (!sideData.TryGetProperty("side_data_type", out var typeProp)) continue;

                    var type = typeProp.GetString()?.ToLowerInvariant() ?? string.Empty;
                    if (type.Contains("2094") || type.Contains("hdr10+") || type.Contains("dynamic metadata"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task RunFFmpegSilenceAndBlackDetectionAsync(string filePath, SilenceDetectionParameters parameters, MediaAnalysisResult result, CancellationToken cancellationToken = default)
    {
        // blackdetect forces a full-frame video decode. The marker assembler only
        // reads gaps from the head (intro/recap) and tail (credits), so when those
        // windows are supplied, decode just [0, headEnd] and [tailStart, end] and
        // skip the middle. Both windows must be set (and disjoint) or we fall back
        // to a single full-file pass.
        if (parameters.HeadWindowEndSeconds is double headEnd &&
            parameters.TailWindowStartSeconds is double tailStart &&
            tailStart > headEnd)
        {
            _logger.LogInformation(
                "Running windowed silence+black detection on {FilePath} (head 0–{HeadEnd}s, tail {TailStart}s–end; threshold {Threshold} dB / {SilenceMin}s, black {BlackMin}s).",
                filePath, headEnd, tailStart, parameters.NoiseThresholdDb, parameters.MinSilenceDurationSec, parameters.MinBlackFrameDurationSec);

            var head = await RunDetectionPassAsync(filePath, parameters, seekSeconds: null, limitSeconds: headEnd, cancellationToken);
            var tail = await RunDetectionPassAsync(filePath, parameters, seekSeconds: tailStart, limitSeconds: null, cancellationToken);

            result.SilenceIntervals = head.Silence.Concat(tail.Silence).ToList();
            result.BlackIntervals = head.Black.Concat(tail.Black).ToList();
        }
        else
        {
            _logger.LogInformation(
                "Running full-file silence+black detection on {FilePath} (threshold {Threshold} dB / {SilenceMin}s, black {BlackMin}s).",
                filePath, parameters.NoiseThresholdDb, parameters.MinSilenceDurationSec, parameters.MinBlackFrameDurationSec);

            var full = await RunDetectionPassAsync(filePath, parameters, seekSeconds: null, limitSeconds: null, cancellationToken);
            result.SilenceIntervals = full.Silence;
            result.BlackIntervals = full.Black;
        }

        _logger.LogInformation(
            "Detection complete: {SilenceCount} silence interval(s), {BlackCount} black interval(s).",
            result.SilenceIntervals.Count, result.BlackIntervals.Count);
    }

    // Runs one ffmpeg silence+black pass over an optional [seekSeconds, +limitSeconds]
    // slice and returns the zipped intervals with absolute timestamps. Input seeking
    // (-ss before -i) is what actually skips the decode; -copyts keeps the reported
    // times on the file's own timeline.
    private async Task<(List<DetectedInterval> Silence, List<DetectedInterval> Black)> RunDetectionPassAsync(
        string filePath, SilenceDetectionParameters parameters, double? seekSeconds, double? limitSeconds, CancellationToken cancellationToken)
    {
        var silenceStarts = new List<double>();
        var silenceEnds = new List<double>();
        var blackStarts = new List<double>();
        var blackEnds = new List<double>();

        var noiseArg = parameters.NoiseThresholdDb.ToString(CultureInfo.InvariantCulture);
        var silenceMin = parameters.MinSilenceDurationSec.ToString(CultureInfo.InvariantCulture);
        var blackMin = parameters.MinBlackFrameDurationSec.ToString(CultureInfo.InvariantCulture);

        var processInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (seekSeconds.HasValue)
        {
            processInfo.ArgumentList.Add("-ss");
            processInfo.ArgumentList.Add(seekSeconds.Value.ToString(CultureInfo.InvariantCulture));
            processInfo.ArgumentList.Add("-copyts");
        }
        processInfo.ArgumentList.Add("-i");
        processInfo.ArgumentList.Add(filePath);
        if (limitSeconds.HasValue)
        {
            processInfo.ArgumentList.Add("-t");
            processInfo.ArgumentList.Add(limitSeconds.Value.ToString(CultureInfo.InvariantCulture));
        }
        processInfo.ArgumentList.Add("-af");
        processInfo.ArgumentList.Add($"silencedetect=noise={noiseArg}dB:d={silenceMin}");
        processInfo.ArgumentList.Add("-vf");
        processInfo.ArgumentList.Add($"blackdetect=d={blackMin}:pix_th=0.10");
        processInfo.ArgumentList.Add("-f");
        processInfo.ArgumentList.Add("null");
        processInfo.ArgumentList.Add("-");

        using var process = new Process { StartInfo = processInfo };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            var sStart = SilenceStartRegex.Match(e.Data);
            if (sStart.Success && double.TryParse(sStart.Groups["Time"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var sStartSec))
                silenceStarts.Add(Math.Max(0, sStartSec));

            var sEnd = SilenceEndRegex.Match(e.Data);
            if (sEnd.Success && double.TryParse(sEnd.Groups["Time"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var sEndSec))
                silenceEnds.Add(Math.Max(0, sEndSec));

            var bStart = BlackStartRegex.Match(e.Data);
            if (bStart.Success && double.TryParse(bStart.Groups["Time"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var bStartSec))
                blackStarts.Add(Math.Max(0, bStartSec));

            var bEnd = BlackEndRegex.Match(e.Data);
            if (bEnd.Success && double.TryParse(bEnd.Groups["Time"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var bEndSec))
                blackEnds.Add(Math.Max(0, bEndSec));
        };

        process.Start();
        process.BeginErrorReadLine();
        await process.WaitForExitWithTimeoutAsync(ProcessTimeout, _logger, cancellationToken);

        return (ZipIntervals(silenceStarts, silenceEnds), ZipIntervals(blackStarts, blackEnds));
    }

    private static List<DetectedInterval> ZipIntervals(List<double> starts, List<double> ends)
    {
        var intervals = new List<DetectedInterval>();
        var count = Math.Min(starts.Count, ends.Count);
        for (var i = 0; i < count; i++)
        {
            if (ends[i] <= starts[i]) continue;
            intervals.Add(new DetectedInterval
            {
                Start = TimeSpan.FromSeconds(starts[i]),
                End = TimeSpan.FromSeconds(ends[i])
            });
        }
        return intervals;
    }
}
