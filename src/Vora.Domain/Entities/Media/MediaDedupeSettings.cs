using Vora.Domain.Entities.Library;

namespace Vora.Domain.Entities.Media;

public class MediaDedupeSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? LibraryId { get; set; }
    public virtual MediaLibrary? Library { get; set; }

    public bool GroupAcrossResolutions { get; set; }

    public int RuntimeToleranceSeconds { get; set; } = 0;
    public long MinimumFileSizeBytes { get; set; } = 0;
    public int MinimumRuntimeSeconds { get; set; } = 0;

    public int ScoreResolution4k { get; set; } = 4000;
    public int ScoreResolution1080 { get; set; } = 2000;
    public int ScoreResolution720 { get; set; } = 1000;
    public int ScoreResolutionOther { get; set; } = 500;

    public int ScoreSourceRemux { get; set; } = 900;
    public int ScoreSourceBluRay { get; set; } = 600;
    public int ScoreSourceWebDl { get; set; } = 400;
    public int ScoreSourceWebRip { get; set; } = 250;
    public int ScoreSourceHdtv { get; set; } = 100;
    public int ScoreSourceDvd { get; set; } = 50;

    public int ScoreCodecAv1 { get; set; } = 800;
    public int ScoreCodecHevc { get; set; } = 600;
    public int ScoreCodecVp9 { get; set; } = 400;
    public int ScoreCodecH264 { get; set; } = 200;

    public int ScoreHdrDolbyVision { get; set; } = 500;
    public int ScoreHdr { get; set; } = 300;

    public int ScoreAudioLossless { get; set; } = 400;
    public int ScoreAudioSurround { get; set; } = 200;
    public int ScoreAudioBase { get; set; } = 50;

    public int ScoreBitrateDivisor { get; set; } = 100000;

    public int ScoreCodecMusicLossless { get; set; } = 2000;
    public int ScoreCodecMusicLossyHigh { get; set; } = 1000;
    public int ScoreCodecMusicLossyStandard { get; set; } = 500;

    public int ScoreSampleRateHi { get; set; } = 300;
    public int ScoreSampleRateStandard { get; set; } = 100;
    public int ScoreSampleRateLow { get; set; }

    public long ScoreFileSizeDivisor { get; set; } = 1_048_576;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
