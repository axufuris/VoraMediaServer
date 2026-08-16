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
    // Black-frame detection only needs to know a frame is (nearly) all black, which
    // is resolution-independent. Downscaling to this height on the GPU before
    // pulling frames back shrinks the GPU→CPU copy ~30x and makes the CPU
    // blackdetect filter cheap — that CPU filtering, not the decode, was the wall.
    private const int BlackDetectHeight = 240;
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

    public async Task<MediaAnalysisResult> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var result = new MediaAnalysisResult();
        await RunFFprobeAsync(filePath, result, cancellationToken);
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

    public async Task<List<MediaChapter>> ReadChaptersAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var chapters = new List<MediaChapter>();

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
        processInfo.ArgumentList.Add("-show_chapters");
        processInfo.ArgumentList.Add(filePath);

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return chapters;

            string jsonOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitWithTimeoutAsync(ProcessTimeout, _logger, cancellationToken);

            if (string.IsNullOrWhiteSpace(jsonOutput)) return chapters;

            using var doc = JsonDocument.Parse(jsonOutput);
            if (!doc.RootElement.TryGetProperty("chapters", out var chaptersBlock)
                || chaptersBlock.ValueKind != JsonValueKind.Array)
            {
                return chapters;
            }

            foreach (var element in chaptersBlock.EnumerateArray())
            {
                if (!TryReadSeconds(element, "start_time", out var start)) continue;
                if (!TryReadSeconds(element, "end_time", out var end)) continue;

                string? title = null;
                if (element.TryGetProperty("tags", out var tags))
                {
                    title = GetTagValue(tags, "title");
                }

                chapters.Add(new MediaChapter
                {
                    Start = TimeSpan.FromSeconds(start),
                    End = TimeSpan.FromSeconds(end),
                    Title = title
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read chapters for {FilePath}; marker detection will fall back to silence/black.", filePath);
        }

        return chapters;
    }

    private static bool TryReadSeconds(JsonElement element, string propertyName, out double seconds)
    {
        seconds = 0;
        if (!element.TryGetProperty(propertyName, out var property)) return false;
        return double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
    }

    public async Task<AudioFingerprintResult?> ExtractAudioFingerprintAsync(string filePath, double startSeconds, double lengthSeconds, string workingDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(workingDirectory);
        var tempWav = Path.Combine(workingDirectory, $"fp_{Guid.NewGuid():N}.wav");

        try
        {
            if (!await ExtractAudioWindowAsync(filePath, startSeconds, lengthSeconds, tempWav, cancellationToken))
            {
                return null;
            }

            var points = await RunFpcalcAsync(tempWav, lengthSeconds, cancellationToken);
            if (points == null || points.Length == 0) return null;

            return new AudioFingerprintResult
            {
                Points = points,
                PointDurationSeconds = lengthSeconds / points.Length
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio fingerprint extraction failed for {FilePath}; intro falls back to silence/black.", filePath);
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(tempWav)) File.Delete(tempWav);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not delete temp fingerprint file {TempWav}.", tempWav);
            }
        }
    }

    private async Task<bool> ExtractAudioWindowAsync(string filePath, double startSeconds, double lengthSeconds, string outputWav, CancellationToken cancellationToken)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        processInfo.ArgumentList.Add("-y");
        processInfo.ArgumentList.Add("-ss");
        processInfo.ArgumentList.Add(startSeconds.ToString(CultureInfo.InvariantCulture));
        processInfo.ArgumentList.Add("-t");
        processInfo.ArgumentList.Add(lengthSeconds.ToString(CultureInfo.InvariantCulture));
        processInfo.ArgumentList.Add("-i");
        processInfo.ArgumentList.Add(filePath);
        processInfo.ArgumentList.Add("-vn");
        processInfo.ArgumentList.Add("-sn");
        // Chromaprint works on downmixed low-rate mono; this shrinks the decode and
        // makes fingerprints comparable regardless of the source's channel layout.
        processInfo.ArgumentList.Add("-ac");
        processInfo.ArgumentList.Add("1");
        processInfo.ArgumentList.Add("-ar");
        processInfo.ArgumentList.Add("11025");
        processInfo.ArgumentList.Add("-f");
        processInfo.ArgumentList.Add("wav");
        processInfo.ArgumentList.Add(outputWav);

        using var process = new Process { StartInfo = processInfo };
        process.ErrorDataReceived += (_, _) => { };
        process.Start();
        process.BeginErrorReadLine();
        var exitedCleanly = await process.WaitForExitWithTimeoutAsync(ProcessTimeout, _logger, cancellationToken);

        return exitedCleanly && process.ExitCode == 0 && File.Exists(outputWav);
    }

    private async Task<uint[]?> RunFpcalcAsync(string wavPath, double lengthSeconds, CancellationToken cancellationToken)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "fpcalc",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        processInfo.ArgumentList.Add("-raw");
        processInfo.ArgumentList.Add("-json");
        // fpcalc fingerprints only the first 120s by default; raise the cap to the
        // whole extracted window.
        processInfo.ArgumentList.Add("-length");
        processInfo.ArgumentList.Add(Math.Ceiling(lengthSeconds + 1).ToString(CultureInfo.InvariantCulture));
        processInfo.ArgumentList.Add(wavPath);

        using var process = Process.Start(processInfo);
        if (process == null) return null;

        var json = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitWithTimeoutAsync(ProcessTimeout, _logger, cancellationToken);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json)) return null;

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("fingerprint", out var fingerprint) || fingerprint.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var points = new uint[fingerprint.GetArrayLength()];
        var index = 0;
        foreach (var element in fingerprint.EnumerateArray())
        {
            points[index++] = unchecked((uint)element.GetInt64());
        }
        return points;
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

    private async Task RunFFprobeAsync(string filePath, MediaAnalysisResult result, CancellationToken cancellationToken = default)
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

            string jsonOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitWithTimeoutAsync(ProcessTimeout, _logger, cancellationToken);

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

            await UpgradeHdr10PlusAsync(filePath, result, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFprobe analysis failed for {FilePath}.", filePath);
            throw;
        }
    }

    private async Task UpgradeHdr10PlusAsync(string filePath, MediaAnalysisResult result, CancellationToken cancellationToken = default)
    {
        var hdr10Tracks = result.VideoTracks
            .Where(t => t.HdrType == "HDR10" || t.HdrType == "DoVi/HDR10")
            .ToList();

        if (hdr10Tracks.Count == 0) return;
        if (!await HasHdr10PlusMetadataAsync(filePath, cancellationToken)) return;

        foreach (var track in hdr10Tracks)
        {
            track.HdrType = track.HdrType == "DoVi/HDR10" ? "DoVi/HDR10Plus" : "HDR10Plus";
        }
    }

    private async Task<bool> HasHdr10PlusMetadataAsync(string filePath, CancellationToken cancellationToken = default)
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

            string jsonOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitWithTimeoutAsync(ProcessTimeout, _logger, cancellationToken);

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
        var (ok, result) = await RunDetectionProcessAsync(filePath, parameters, seekSeconds, limitSeconds, parameters.UseHardwareDecode, cancellationToken);

        // The hardware path forces CUDA/NVDEC, so a codec/profile it can't handle
        // (or a box without an NVIDIA GPU) errors the pass — retry in pure software
        // so a GPU quirk never drops an item's markers.
        if (!ok && parameters.UseHardwareDecode && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Hardware-accelerated detection pass failed for {Input}; retrying in software.", filePath);
            (_, result) = await RunDetectionProcessAsync(filePath, parameters, seekSeconds, limitSeconds, useHardware: false, cancellationToken);
        }

        return result;
    }

    private async Task<(bool Ok, (List<DetectedInterval> Silence, List<DetectedInterval> Black) Result)> RunDetectionProcessAsync(
        string filePath, SilenceDetectionParameters parameters, double? seekSeconds, double? limitSeconds, bool useHardware, CancellationToken cancellationToken)
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
        // Hardware path: decode on NVDEC, keep frames on the GPU, downscale +
        // convert to 8-bit there, THEN pull the small frames back for the CPU
        // blackdetect. This is the win — the CPU was filtering full-res frames.
        // -hwaccel is an input option and must precede -ss/-i.
        if (useHardware)
        {
            processInfo.ArgumentList.Add("-hwaccel");
            processInfo.ArgumentList.Add("cuda");
            processInfo.ArgumentList.Add("-hwaccel_output_format");
            processInfo.ArgumentList.Add("cuda");
            if (!string.IsNullOrWhiteSpace(parameters.HardwareDevice) && parameters.HardwareDevice != "Auto")
            {
                processInfo.ArgumentList.Add("-hwaccel_device");
                processInfo.ArgumentList.Add(parameters.HardwareDevice);
            }
        }
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
        var blackFilter = $"blackdetect=d={blackMin}:pix_th=0.10";
        processInfo.ArgumentList.Add("-vf");
        processInfo.ArgumentList.Add(useHardware
            ? $"scale_cuda=-2:{BlackDetectHeight}:format=nv12,hwdownload,format=nv12,{blackFilter}"
            : blackFilter);
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
        var exited = await process.WaitForExitWithTimeoutAsync(ProcessTimeout, _logger, cancellationToken);
        var ok = exited && process.ExitCode == 0;

        return (ok, (ZipIntervals(silenceStarts, silenceEnds), ZipIntervals(blackStarts, blackEnds)));
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
