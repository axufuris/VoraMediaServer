using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Application.Streaming.Requests;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class PlaybackChoiceManagerTests
{
    private static readonly Guid Profile = Guid.NewGuid();
    private const string Device = "living-room";

    private readonly IStreamRepository _repository = Substitute.For<IStreamRepository>();
    private readonly Guid _episodeId = Guid.NewGuid();
    private readonly Guid _showId = Guid.NewGuid();
    private readonly MediaPartStreamInfoDto _part;
    private readonly TrackStreamInfoDto _japanese;
    private readonly SubtitleStreamInfoDto _englishSdh;

    public PlaybackChoiceManagerTests()
    {
        _japanese = new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "aac", Channels = 2, Language = "jpn" };
        _englishSdh = new SubtitleStreamInfoDto { Id = Guid.NewGuid(), Codec = "subrip", Language = "eng", IsHearingImpaired = true };
        _part = new MediaPartStreamInfoDto
        {
            Id = Guid.NewGuid(), Resolution = "2160p", Edition = "Extended",
            VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "hevc", HdrType = "HDR10", IsDefault = true }],
            AudioTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "eac3", Channels = 6, Language = "eng", IsDefault = true }, _japanese],
            SubtitleTracks = [_englishSdh]
        };

        _repository.ResolvePlayableMediaIdAsync(_episodeId, Profile).Returns(_episodeId);
        _repository.GetMediaStreamInfoAsync(_episodeId).Returns(new MediaStreamInfoDto { Id = _episodeId, Parts = [_part] });
        _repository.ResolveTitleIdAsync(_episodeId).Returns(_showId);
    }

    private PlaybackChoiceManager Manager() => new(_repository);

    [Fact]
    public async Task Picks_are_remembered_by_what_they_are_for_the_whole_show_on_this_device()
    {
        await Manager().RememberAsync(Profile, Device, _episodeId, new RememberChoiceRequest { MediaPartId = _part.Id, AudioTrackId = _japanese.Id, SubtitleTrackId = _englishSdh.Id });

        await _repository.Received(1).SaveRememberedChoiceAsync(Arg.Is<ProfileTitlePlaybackChoice>(choice =>
            choice.ProfileId == Profile && choice.DeviceId == Device && choice.TitleId == _showId
            && choice.RemembersVersion && choice.Edition == "Extended" && choice.VersionHeight == 2160 && choice.VideoCodec == "hevc" && choice.HdrType == "HDR10"
            && choice.RemembersAudio && choice.AudioLanguage == "jpn" && choice.AudioCodec == "aac" && choice.AudioChannels == 2 && !choice.AudioIsCommentary
            && choice.Subtitle == RememberedSubtitle.Language && choice.SubtitleLanguage == "eng" && choice.SubtitleIsHearingImpaired));
    }

    [Fact]
    public async Task Going_back_to_automatic_for_one_kind_keeps_the_others()
    {
        _repository.GetRememberedChoiceAsync(Profile, Device, _showId).Returns(new ProfileTitlePlaybackChoice
        {
            ProfileId = Profile, DeviceId = Device, TitleId = _showId, RemembersAudio = true, AudioLanguage = "jpn", Subtitle = RememberedSubtitle.Off
        });

        var remembered = await Manager().RememberAsync(Profile, Device, _episodeId, new RememberChoiceRequest { Forget = [PlaybackChoiceKind.Audio] });

        remembered.RemembersAudio.Should().BeFalse();
        remembered.Subtitle.Should().Be(RememberedSubtitle.Off);
        await _repository.Received(1).SaveRememberedChoiceAsync(Arg.Is<ProfileTitlePlaybackChoice>(choice => !choice.RemembersAudio && choice.Subtitle == RememberedSubtitle.Off));
    }

    [Fact]
    public async Task Nothing_left_to_remember_removes_the_title_for_this_device()
    {
        _repository.GetRememberedChoiceAsync(Profile, Device, _showId).Returns(new ProfileTitlePlaybackChoice
        {
            ProfileId = Profile, DeviceId = Device, TitleId = _showId, RemembersAudio = true, AudioLanguage = "jpn"
        });

        await Manager().RememberAsync(Profile, Device, _episodeId, new RememberChoiceRequest { Forget = [PlaybackChoiceKind.Audio] });

        await _repository.Received(1).ForgetRememberedChoiceAsync(Profile, Device, _showId);
        await _repository.DidNotReceive().SaveRememberedChoiceAsync(Arg.Any<ProfileTitlePlaybackChoice>());
    }

    [Fact]
    public async Task A_track_from_another_title_is_refused()
    {
        var act = () => Manager().RememberAsync(Profile, Device, _episodeId, new RememberChoiceRequest { AudioTrackId = Guid.NewGuid() });

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _repository.DidNotReceive().SaveRememberedChoiceAsync(Arg.Any<ProfileTitlePlaybackChoice>());
    }

    [Fact]
    public async Task Subtitles_off_is_remembered_as_its_own_choice()
    {
        var remembered = await Manager().RememberAsync(Profile, Device, _episodeId, new RememberChoiceRequest { SubtitlesOff = true });

        remembered.Subtitle.Should().Be(RememberedSubtitle.Off);
        remembered.RemembersAudio.Should().BeFalse();
    }
}
