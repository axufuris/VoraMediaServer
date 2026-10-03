using System.Globalization;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

public static class AudioQuality
{
    public const int HiResAboveSampleRate = 48_000;

    private static readonly HashSet<string> LosslessCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "flac", "alac", "ape", "wavpack", "tta", "mlp", "truehd"
    };

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["flac"] = "FLAC",
        ["alac"] = "ALAC",
        ["ape"] = "APE",
        ["wavpack"] = "WavPack",
        ["tta"] = "TTA",
        ["mp3"] = "MP3",
        ["aac"] = "AAC",
        ["opus"] = "Opus",
        ["vorbis"] = "Ogg Vorbis",
        ["wmav2"] = "WMA",
        ["wmapro"] = "WMA Pro",
    };

    public static AudioQualityVM? For(string? codec, int? sampleRate, int? bitrate)
    {
        if (string.IsNullOrWhiteSpace(codec)) return null;

        var name = codec.Trim().ToLowerInvariant();
        var isPcm = name.StartsWith("pcm_", StringComparison.Ordinal);
        var isDsd = name.StartsWith("dsd_", StringComparison.Ordinal);
        var lossless = isPcm || isDsd || LosslessCodecs.Contains(name);
        var format = isPcm ? "PCM" : isDsd ? "DSD" : Names.TryGetValue(name, out var known) ? known : name.ToUpperInvariant();
        var rate = sampleRate is > 0 ? sampleRate : null;
        var kbps = !lossless && bitrate is > 8 ? bitrate : null;
        var hiRes = lossless && rate > HiResAboveSampleRate;

        var detail = lossless
            ? rate is int r ? $"{format} {Khz(r)}" : format
            : kbps is int k ? $"{format} · {k} kbps" : format;

        return new AudioQualityVM
        {
            Format = format,
            SampleRate = rate,
            Bitrate = kbps,
            Lossless = lossless,
            HiRes = hiRes,
            Label = lossless ? $"{(hiRes ? "Hi-Res" : "Lossless")} · {detail}" : detail
        };
    }

    public static AudioQualityVM? ForAlbum(IEnumerable<Track> tracks)
    {
        var qualities = tracks
            .Select(t => For(t.AudioCodec, t.SampleRate, t.Bitrate))
            .OfType<AudioQualityVM>()
            .ToList();
        if (qualities.Count == 0) return null;

        var common = qualities
            .GroupBy(q => q.Format)
            .OrderByDescending(g => g.Count())
            .First()
            .ToList();

        var representative = common[0].Lossless
            ? common.OrderByDescending(q => q.SampleRate ?? 0).First()
            : common.GroupBy(q => q.Bitrate).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key ?? 0).First().First();

        return representative;
    }

    private static string Khz(int sampleRate)
    {
        var khz = sampleRate / 1000m;
        return $"{khz.ToString(khz == decimal.Truncate(khz) ? "0" : "0.#", CultureInfo.InvariantCulture)} kHz";
    }
}
