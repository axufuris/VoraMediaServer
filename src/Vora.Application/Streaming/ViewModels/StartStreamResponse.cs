namespace Vora.Application.Streaming.ViewModels;

public class StartStreamResponse
{
    public required Guid SessionId { get; set; }
    public required string StreamUrl { get; set; }
    public required Guid VideoTrackId { get; set; }
    public required Guid AudioTrackId { get; set; }
    public Guid? SubtitleTrackId { get; set; }
    public required string Strategy { get; set; }
    public required string VideoStrategy { get; set; }
    public required string AudioStrategy { get; set; }
    public required string SubtitleStrategy { get; set; }
    public required string VideoCodec { get; set; }
    public required string AudioCodec { get; set; }
    public required string Container { get; set; }
    public required int BandwidthKbps { get; set; }
    public required int TargetAudioChannels { get; set; }
    // Output-side stream info the player uses to render its badge bar.
    // OutputResolution and OutputHdrType are what's actually delivered
    // (e.g. "1080p" + "SDR" when a 4K HDR source is downscaled and
    // tonemapped via the HDR transcode pipeline).
    public string? OutputResolution { get; set; }
    public string? OutputHdrType { get; set; }
}
