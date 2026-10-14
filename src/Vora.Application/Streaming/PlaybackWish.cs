using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Plugins;

namespace Vora.Application.Streaming;

public sealed record VersionWish(Guid? PartId, Guid? VideoTrackId, string? Edition, int Height, string? VideoCodec, string? HdrType);

public sealed record AudioWish(Guid? TrackId, string? Language, bool IsCommentary, string? Codec, int Channels);

public sealed record SubtitleWish(Guid? TrackId, bool Off, string? Language, bool IsForced, bool IsHearingImpaired);

public sealed class PlaybackWish
{
    private const int UnknownChannels = 2;

    public PlaybackWish(VersionWish? version, AudioWish? audio, SubtitleWish? subtitle)
    {
        Version = version;
        Audio = audio;
        Subtitle = subtitle;
    }

    public VersionWish? Version { get; }
    public AudioWish? Audio { get; }
    public SubtitleWish? Subtitle { get; }

    public bool NarrowsChoice => Version != null || Audio != null;

    public static PlaybackWish For(MediaStreamInfoDto media, Guid? requestedPartId, Guid? requestedVideoId, Guid? requestedAudioId, Guid? requestedSubtitleId, ProfileTitlePlaybackChoice? remembered)
    {
        var version = RequestedVersion(media, requestedPartId, requestedVideoId) ?? RememberedVersion(remembered);
        var audio = RequestedAudio(media, requestedAudioId) ?? RememberedAudio(remembered);
        var subtitle = requestedSubtitleId.HasValue ? RequestedSubtitle(media, requestedSubtitleId.Value) : RememberedSubtitleWish(remembered);

        var wish = new PlaybackWish(version, audio, subtitle);
        if (version != null && !media.Parts.Any(wish.VersionFamily)) version = null;
        if (audio != null && !media.Parts.Any(part => part.AudioTracks.Any(wish.AudioFamily))) audio = null;
        return new PlaybackWish(version, audio, subtitle);
    }

    public bool VersionFamily(MediaPartStreamInfoDto part) =>
        Version == null || SameText(part.Edition, Version.Edition);

    public bool VersionExact(MediaPartStreamInfoDto part, TrackStreamInfoDto video)
    {
        if (Version == null) return true;
        if (Version.PartId.HasValue) return part.Id == Version.PartId && (Version.VideoTrackId == null || video.Id == Version.VideoTrackId);
        return VersionFamily(part)
            && BestPathDecisionManager.ParseHeightFromResolution(part.Resolution) == Version.Height
            && SameText(video.Codec, Version.VideoCodec)
            && SameText(Hdr(video.HdrType), Hdr(Version.HdrType));
    }

    public bool AudioFamily(TrackStreamInfoDto track) =>
        Audio == null || (SameLanguage(track.Language, Audio.Language) && IsCommentary(track) == Audio.IsCommentary);

    public bool AudioExact(TrackStreamInfoDto track) =>
        Audio == null
        || track.Id == Audio.TrackId
        || (AudioFamily(track) && SameText(track.Codec, Audio.Codec) && Channels(track) == Audio.Channels);

    public SubtitleStreamInfoDto? SubtitleFor(MediaPartStreamInfoDto part, Func<SubtitleStreamInfoDto?> automatic)
    {
        if (Subtitle == null) return automatic();
        if (Subtitle.Off) return null;

        return part.SubtitleTracks.FirstOrDefault(track => track.Id == Subtitle.TrackId)
            ?? part.SubtitleTracks.FirstOrDefault(track => SameLanguage(track.Language, Subtitle.Language)
                && track.IsForced == Subtitle.IsForced
                && track.IsHearingImpaired == Subtitle.IsHearingImpaired)
            ?? automatic();
    }

    public static bool IsCommentary(TrackStreamInfoDto track) =>
        track.Title?.Contains("commentary", StringComparison.OrdinalIgnoreCase) == true;

    public static int Channels(TrackStreamInfoDto track) => track.Channels is > 0 ? track.Channels.Value : UnknownChannels;

    private static VersionWish? RequestedVersion(MediaStreamInfoDto media, Guid? partId, Guid? videoTrackId)
    {
        var part = partId.HasValue
            ? media.Parts.FirstOrDefault(candidate => candidate.Id == partId.Value)
            : videoTrackId.HasValue ? media.Parts.FirstOrDefault(candidate => candidate.VideoTracks.Any(track => track.Id == videoTrackId.Value)) : null;
        if (part == null) return null;

        var picked = part.VideoTracks.FirstOrDefault(track => track.Id == videoTrackId);
        var video = picked ?? part.VideoTracks.FirstOrDefault(track => track.IsDefault) ?? part.VideoTracks.FirstOrDefault();
        return new VersionWish(part.Id, picked?.Id, part.Edition, BestPathDecisionManager.ParseHeightFromResolution(part.Resolution), video?.Codec, video?.HdrType);
    }

    private static AudioWish? RequestedAudio(MediaStreamInfoDto media, Guid? trackId)
    {
        if (!trackId.HasValue) return null;
        var track = media.Parts.SelectMany(part => part.AudioTracks).FirstOrDefault(candidate => candidate.Id == trackId.Value);
        return track == null ? null : new AudioWish(track.Id, track.Language, IsCommentary(track), track.Codec, Channels(track));
    }

    private static SubtitleWish RequestedSubtitle(MediaStreamInfoDto media, Guid trackId)
    {
        var track = trackId == Guid.Empty ? null : media.Parts.SelectMany(part => part.SubtitleTracks).FirstOrDefault(candidate => candidate.Id == trackId);
        return track == null
            ? new SubtitleWish(null, true, null, false, false)
            : new SubtitleWish(track.Id, false, track.Language, track.IsForced, track.IsHearingImpaired);
    }

    private static VersionWish? RememberedVersion(ProfileTitlePlaybackChoice? remembered) =>
        remembered is { RemembersVersion: true }
            ? new VersionWish(null, null, remembered.Edition, remembered.VersionHeight, remembered.VideoCodec, remembered.HdrType)
            : null;

    private static AudioWish? RememberedAudio(ProfileTitlePlaybackChoice? remembered) =>
        remembered is { RemembersAudio: true }
            ? new AudioWish(null, remembered.AudioLanguage, remembered.AudioIsCommentary, remembered.AudioCodec, remembered.AudioChannels)
            : null;

    private static SubtitleWish? RememberedSubtitleWish(ProfileTitlePlaybackChoice? remembered)
    {
        if (remembered == null) return null;
        return remembered.Subtitle switch
        {
            RememberedSubtitle.Off => new SubtitleWish(null, true, null, false, false),
            RememberedSubtitle.Language => new SubtitleWish(null, false, remembered.SubtitleLanguage, remembered.SubtitleIsForced, remembered.SubtitleIsHearingImpaired),
            _ => null
        };
    }

    private static bool SameLanguage(string? left, string? right) =>
        MetadataLanguageCodes.Normalize(left) == MetadataLanguageCodes.Normalize(right);

    private static bool SameText(string? left, string? right) =>
        string.Equals(left?.Trim() ?? string.Empty, right?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string Hdr(string? hdrType) =>
        string.IsNullOrWhiteSpace(hdrType) || hdrType.Equals("SDR", StringComparison.OrdinalIgnoreCase) || hdrType.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? "SDR"
            : hdrType;
}
