namespace Vora.Application.Media.ViewModels;

public class DedupeSettingsVM
{
    public Guid? LibraryId { get; set; }

    public bool GroupAcrossResolutions { get; set; }

    public int RuntimeToleranceSeconds { get; set; }
    public long MinimumFileSizeBytes { get; set; }
    public int MinimumRuntimeSeconds { get; set; }

    public int ScoreResolution4k { get; set; }
    public int ScoreResolution1080 { get; set; }
    public int ScoreResolution720 { get; set; }
    public int ScoreResolutionOther { get; set; }

    public int ScoreSourceRemux { get; set; }
    public int ScoreSourceBluRay { get; set; }
    public int ScoreSourceWebDl { get; set; }
    public int ScoreSourceWebRip { get; set; }
    public int ScoreSourceHdtv { get; set; }
    public int ScoreSourceDvd { get; set; }

    public int ScoreCodecAv1 { get; set; }
    public int ScoreCodecHevc { get; set; }
    public int ScoreCodecVp9 { get; set; }
    public int ScoreCodecH264 { get; set; }

    public int ScoreHdrDolbyVision { get; set; }
    public int ScoreHdr { get; set; }
    public int ScoreHdr10PlusBonus { get; set; }

    public int ScoreAudioLossless { get; set; }
    public int ScoreAudioSurround { get; set; }
    public int ScoreAudioBase { get; set; }

    public int ScoreBitrateDivisor { get; set; }

    public int ScoreCodecMusicLossless { get; set; }
    public int ScoreCodecMusicLossyHigh { get; set; }
    public int ScoreCodecMusicLossyStandard { get; set; }

    public int ScoreSampleRateHi { get; set; }
    public int ScoreSampleRateStandard { get; set; }
    public int ScoreSampleRateLow { get; set; }

    public long ScoreFileSizeDivisor { get; set; }

    public bool IsDefault { get; set; }
}
