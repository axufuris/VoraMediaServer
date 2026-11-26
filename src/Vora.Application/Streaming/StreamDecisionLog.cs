using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Streaming;

/// <summary>
/// One line for the server log saying what a client claimed it can play, what the file is and what the
/// server chose, so a "plays but shows no picture" or "audio drops out" report can be read from the log
/// without the device. Names no file path, address, account or token.
/// </summary>
public static class StreamDecisionLog
{
    public static string Describe(
        ClientDevice client,
        DeviceCapsDto? capabilities,
        MediaPartStreamInfoDto? part,
        TrackStreamInfoDto? video,
        TrackStreamInfoDto? audio,
        StreamDecisionDto decision)
    {
        var hlsReported = capabilities?.HlsVideoCodecs is { Length: > 0 };
        var hls = HlsVideoCarriage.For(client.DeviceType, capabilities?.HlsVideoCodecs);

        var claims = string.Join(' ',
            $"video={List(client.SupportedVideoCodecs)}",
            $"hlsVideo={List(hls.Order(StringComparer.Ordinal))}{(hlsReported ? "" : "(default for this device type)")}",
            $"audio={List(client.SupportedAudioCodecs)}",
            $"containers={List(client.SupportedContainers)}",
            $"bitDepth={(client.MaxVideoBitDepth > 0 ? client.MaxVideoBitDepth : "-")}",
            $"channels={client.MaxAudioChannels}",
            $"hdr={List(client.SupportedHdrFormats)}");

        var source = string.Join(' ',
            $"container={Text(part?.Container)}",
            $"video={Text(video?.Codec)}",
            $"{(video?.Width is { } w && video.Height is { } h ? $"{w}x{h}" : "-")}",
            $"bitDepth={(video?.BitDepth is { } depth ? depth : "-")}",
            $"hdr={Text(video?.HdrType)}",
            $"audio={Text(audio?.Codec)}",
            $"channels={(audio?.Channels is { } channels ? channels : "-")}");

        var chosen = string.Join(' ',
            $"strategy={decision.Strategy}",
            $"video={Text(decision.VideoStrategy)}->{Text(decision.TargetVideoCodec)}",
            $"audio={Text(decision.AudioStrategy)}->{Text(decision.TargetAudioCodec)}",
            $"container={Text(decision.TargetContainer)}");

        var reasons = decision.Reasons.Count == 0 ? "-" : string.Join(" | ", decision.Reasons);
        return $"client={Text(client.DeviceType)}/{Text(client.ClientName)} {Text(client.OperatingSystem)}; claims: {claims}; source: {source}; chose: {chosen}; reasons: {reasons}";
    }

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string List(IEnumerable<string>? values)
    {
        var items = values?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return items is { Count: > 0 } ? $"[{string.Join(',', items)}]" : "-";
    }
}
