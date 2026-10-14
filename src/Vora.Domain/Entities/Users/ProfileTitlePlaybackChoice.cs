using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Users;

public class ProfileTitlePlaybackChoice
{
    public Guid ProfileId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public Guid TitleId { get; set; }

    public bool RemembersVersion { get; set; }
    public string? Edition { get; set; }
    public int VersionHeight { get; set; }
    public string? VideoCodec { get; set; }
    public string? HdrType { get; set; }

    public bool RemembersAudio { get; set; }
    public string? AudioLanguage { get; set; }
    public bool AudioIsCommentary { get; set; }
    public string? AudioCodec { get; set; }
    public int AudioChannels { get; set; }

    public RememberedSubtitle Subtitle { get; set; }
    public string? SubtitleLanguage { get; set; }
    public bool SubtitleIsForced { get; set; }
    public bool SubtitleIsHearingImpaired { get; set; }

    public DateTime UpdatedAt { get; set; }
}
