using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Streaming.ViewModels;

public sealed class RememberedChoiceVM
{
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

    public static RememberedChoiceVM From(ProfileTitlePlaybackChoice? choice) => choice == null
        ? new RememberedChoiceVM()
        : new RememberedChoiceVM
        {
            RemembersVersion = choice.RemembersVersion,
            Edition = choice.Edition,
            VersionHeight = choice.VersionHeight,
            VideoCodec = choice.VideoCodec,
            HdrType = choice.HdrType,
            RemembersAudio = choice.RemembersAudio,
            AudioLanguage = choice.AudioLanguage,
            AudioIsCommentary = choice.AudioIsCommentary,
            AudioCodec = choice.AudioCodec,
            AudioChannels = choice.AudioChannels,
            Subtitle = choice.Subtitle,
            SubtitleLanguage = choice.SubtitleLanguage,
            SubtitleIsForced = choice.SubtitleIsForced,
            SubtitleIsHearingImpaired = choice.SubtitleIsHearingImpaired,
        };
}
