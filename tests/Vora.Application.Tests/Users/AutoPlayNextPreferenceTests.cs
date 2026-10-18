using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Vora.Application.Analysis;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Users;

public class AutoPlayNextPreferenceTests
{
    private readonly IUserRepository _repo = Substitute.For<IUserRepository>();
    private readonly UserManager _manager;
    private readonly UserProfile _profile = new() { Id = Guid.NewGuid(), Name = "Andy", UserId = Guid.NewGuid() };

    public AutoPlayNextPreferenceTests()
    {
        _manager = new UserManager(_repo, Substitute.For<IUserProfileImageService>(), Substitute.For<IClientNotifier>(), NullLogger<UserManager>.Instance);
        _repo.GetProfileByIdAsync(_profile.Id).Returns(_profile);
    }

    [Fact]
    public async Task A_new_profile_plays_the_next_episode_automatically()
    {
        var prefs = await _manager.GetPlaybackPreferencesAsync(_profile.Id);

        prefs.AutoPlayNext.Should().BeTrue();
    }

    [Fact]
    public async Task Turning_it_off_is_saved_beside_the_auto_skip_settings()
    {
        var saved = await _manager.SavePlaybackPreferencesAsync(_profile.Id, new PlaybackPreferencesVM { AutoSkipCredits = true, AutoPlayNext = false, MinimumCreditsSceneSeconds = 20 });

        saved.AutoPlayNext.Should().BeFalse();
        saved.AutoSkipCredits.Should().BeTrue();
        _profile.AutoPlayNext.Should().BeFalse();
        await _repo.Received(1).UpdateProfileAsync(_profile);
    }

    [Fact]
    public async Task An_app_that_does_not_send_it_leaves_it_as_it_was()
    {
        _profile.AutoPlayNext = false;

        var saved = await _manager.SavePlaybackPreferencesAsync(_profile.Id, new PlaybackPreferencesVM { AutoSkipIntro = true, MinimumCreditsSceneSeconds = 15 });

        saved.AutoPlayNext.Should().BeFalse();
        saved.AutoSkipIntro.Should().BeTrue();
        _profile.AutoPlayNext.Should().BeFalse();
    }
}
