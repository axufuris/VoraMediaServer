using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Vora.Application.Analysis;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Users;

public class EpisodePreferenceTests
{
    private readonly IUserRepository _repo = Substitute.For<IUserRepository>();
    private readonly UserManager _manager;
    private readonly UserProfile _profile = new() { Id = Guid.NewGuid(), Name = "Andy", UserId = Guid.NewGuid() };

    public EpisodePreferenceTests()
    {
        _manager = new UserManager(_repo, Substitute.For<IUserProfileImageService>(), Substitute.For<IClientNotifier>(), NullLogger<UserManager>.Instance);
        _repo.GetProfileByIdAsync(_profile.Id).Returns(_profile);
    }

    [Fact]
    public async Task A_new_profile_plays_episodes_in_order_and_shows_them_as_a_row()
    {
        var prefs = await _manager.GetPlaybackPreferencesAsync(_profile.Id);

        prefs.UpNextOrder.Should().Be(UpNextOrder.InOrder);
        prefs.EpisodeLayout.Should().Be(EpisodeLayout.Row);
    }

    [Fact]
    public async Task Both_choices_are_saved_on_the_profile()
    {
        var saved = await _manager.SavePlaybackPreferencesAsync(_profile.Id, new PlaybackPreferencesVM
        {
            MinimumCreditsSceneSeconds = 20, UpNextOrder = UpNextOrder.WhereILeftOff, EpisodeLayout = EpisodeLayout.List,
        });

        (saved.UpNextOrder, saved.EpisodeLayout).Should().Be((UpNextOrder.WhereILeftOff, EpisodeLayout.List));
        (_profile.UpNextOrder, _profile.EpisodeLayout).Should().Be((UpNextOrder.WhereILeftOff, EpisodeLayout.List));
        await _repo.Received(1).UpdateProfileAsync(_profile);
    }

    [Fact]
    public async Task An_app_that_does_not_send_them_leaves_them_as_they_were()
    {
        _profile.UpNextOrder = UpNextOrder.WhereILeftOff;
        _profile.EpisodeLayout = EpisodeLayout.List;

        var saved = await _manager.SavePlaybackPreferencesAsync(_profile.Id, new PlaybackPreferencesVM { AutoSkipIntro = true, MinimumCreditsSceneSeconds = 15 });

        (saved.UpNextOrder, saved.EpisodeLayout).Should().Be((UpNextOrder.WhereILeftOff, EpisodeLayout.List));
    }

    [Fact]
    public async Task An_unknown_order_is_refused()
    {
        var save = () => _manager.SavePlaybackPreferencesAsync(_profile.Id, new PlaybackPreferencesVM { MinimumCreditsSceneSeconds = 15, UpNextOrder = (UpNextOrder)7 });

        await save.Should().ThrowAsync<ArgumentException>();
        await _repo.DidNotReceive().UpdateProfileAsync(_profile);
    }

    [Fact]
    public async Task An_unknown_layout_is_refused()
    {
        var save = () => _manager.SavePlaybackPreferencesAsync(_profile.Id, new PlaybackPreferencesVM { MinimumCreditsSceneSeconds = 15, EpisodeLayout = (EpisodeLayout)7 });

        await save.Should().ThrowAsync<ArgumentException>();
        await _repo.DidNotReceive().UpdateProfileAsync(_profile);
    }
}
