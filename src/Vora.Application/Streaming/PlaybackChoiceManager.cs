using Vora.Application.Streaming.Dtos;
using Vora.Application.Streaming.Requests;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Streaming;

public interface IPlaybackChoiceManager
{
    Task<RememberedChoiceVM> GetAsync(Guid profileId, string deviceId, Guid mediaId);
    Task<RememberedChoiceVM> RememberAsync(Guid profileId, string deviceId, Guid mediaId, RememberChoiceRequest request);
    Task ForgetAsync(Guid profileId, string deviceId, Guid mediaId);
}

public sealed class PlaybackChoiceManager : IPlaybackChoiceManager
{
    private readonly IStreamRepository _repository;

    public PlaybackChoiceManager(IStreamRepository repository)
    {
        _repository = repository;
    }

    public async Task<RememberedChoiceVM> GetAsync(Guid profileId, string deviceId, Guid mediaId)
    {
        var titleId = await _repository.ResolveTitleIdAsync(mediaId);
        return RememberedChoiceVM.From(await _repository.GetRememberedChoiceAsync(profileId, deviceId, titleId));
    }

    public async Task<RememberedChoiceVM> RememberAsync(Guid profileId, string deviceId, Guid mediaId, RememberChoiceRequest request)
    {
        var playableId = await _repository.ResolvePlayableMediaIdAsync(mediaId, profileId);
        var media = await _repository.GetMediaStreamInfoAsync(playableId)
            ?? throw new InvalidOperationException("Media not found.");
        var titleId = await _repository.ResolveTitleIdAsync(playableId);
        var choice = await _repository.GetRememberedChoiceAsync(profileId, deviceId, titleId)
            ?? new ProfileTitlePlaybackChoice { ProfileId = profileId, DeviceId = deviceId, TitleId = titleId };

        if (request.MediaPartId.HasValue) RememberVersion(choice, media, request.MediaPartId.Value);
        if (request.AudioTrackId.HasValue) RememberAudio(choice, media, request.AudioTrackId.Value);
        if (request.SubtitlesOff) RememberSubtitles(choice, null);
        else if (request.SubtitleTrackId.HasValue) RememberSubtitles(choice, Track(media.Parts.SelectMany(part => part.SubtitleTracks), request.SubtitleTrackId.Value, track => track.Id, "subtitle"));
        foreach (var kind in request.Forget) Forget(choice, kind);

        if (!choice.RemembersVersion && !choice.RemembersAudio && choice.Subtitle == RememberedSubtitle.Automatic)
        {
            await _repository.ForgetRememberedChoiceAsync(profileId, deviceId, titleId);
            return new RememberedChoiceVM();
        }

        choice.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveRememberedChoiceAsync(choice);
        return RememberedChoiceVM.From(choice);
    }

    public async Task ForgetAsync(Guid profileId, string deviceId, Guid mediaId) =>
        await _repository.ForgetRememberedChoiceAsync(profileId, deviceId, await _repository.ResolveTitleIdAsync(mediaId));

    private static void RememberVersion(ProfileTitlePlaybackChoice choice, MediaStreamInfoDto media, Guid partId)
    {
        var part = Track(media.Parts, partId, candidate => candidate.Id, "version");
        var video = part.VideoTracks.FirstOrDefault(track => track.IsDefault) ?? part.VideoTracks.FirstOrDefault();
        choice.RemembersVersion = true;
        choice.Edition = part.Edition;
        choice.VersionHeight = BestPathDecisionManager.ParseHeightFromResolution(part.Resolution);
        choice.VideoCodec = video?.Codec;
        choice.HdrType = video?.HdrType;
    }

    private static void RememberAudio(ProfileTitlePlaybackChoice choice, MediaStreamInfoDto media, Guid trackId)
    {
        var track = Track(media.Parts.SelectMany(part => part.AudioTracks), trackId, candidate => candidate.Id, "audio track");
        choice.RemembersAudio = true;
        choice.AudioLanguage = track.Language;
        choice.AudioIsCommentary = PlaybackWish.IsCommentary(track);
        choice.AudioCodec = track.Codec;
        choice.AudioChannels = PlaybackWish.Channels(track);
    }

    private static void RememberSubtitles(ProfileTitlePlaybackChoice choice, SubtitleStreamInfoDto? track)
    {
        choice.Subtitle = track == null ? RememberedSubtitle.Off : RememberedSubtitle.Language;
        choice.SubtitleLanguage = track?.Language;
        choice.SubtitleIsForced = track?.IsForced ?? false;
        choice.SubtitleIsHearingImpaired = track?.IsHearingImpaired ?? false;
    }

    private static void Forget(ProfileTitlePlaybackChoice choice, PlaybackChoiceKind kind)
    {
        switch (kind)
        {
            case PlaybackChoiceKind.Version:
                choice.RemembersVersion = false;
                choice.Edition = null;
                choice.VersionHeight = 0;
                choice.VideoCodec = null;
                choice.HdrType = null;
                break;
            case PlaybackChoiceKind.Audio:
                choice.RemembersAudio = false;
                choice.AudioLanguage = null;
                choice.AudioIsCommentary = false;
                choice.AudioCodec = null;
                choice.AudioChannels = 0;
                break;
            case PlaybackChoiceKind.Subtitles:
                choice.Subtitle = RememberedSubtitle.Automatic;
                choice.SubtitleLanguage = null;
                choice.SubtitleIsForced = false;
                choice.SubtitleIsHearingImpaired = false;
                break;
        }
    }

    private static T Track<T>(IEnumerable<T> candidates, Guid id, Func<T, Guid> idOf, string noun) =>
        candidates.FirstOrDefault(candidate => idOf(candidate) == id)
        ?? throw new InvalidOperationException($"That {noun} is not part of this title.");
}
