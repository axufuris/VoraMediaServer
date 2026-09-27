using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Vora.Application.Analysis;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Users;

public class ProfileSettingsPreservationTests
{
    private readonly IUserRepository _repo = Substitute.For<IUserRepository>();
    private readonly UserManager _manager;

    public ProfileSettingsPreservationTests()
    {
        _manager = new UserManager(_repo, Substitute.For<IUserProfileImageService>(), Substitute.For<IClientNotifier>(), NullLogger<UserManager>.Instance);
    }

    private UserProfile Profile(string? showtimesLocation) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Andy",
        UserId = Guid.NewGuid(),
        ShowtimesLocation = showtimesLocation
    };

    private async Task UpdateAsync(UserProfile profile, string? showtimesLocation, bool canRecordLiveTv = false)
    {
        _repo.GetProfileByIdAsync(profile.Id).Returns(profile);
        await _manager.UpdateManagedProfileAsync(
            profile.Id, profile.Name, null, null,
            new List<string>(), new List<string>(), new List<string>(),
            true, false, new List<Guid>(), true, new List<Guid>(),
            new List<ProfileScheduleVM>(), true, canRecordLiveTv, showtimesLocation);
    }

    [Fact]
    public async Task An_editor_that_omits_the_showtimes_location_leaves_it_alone()
    {
        var profile = Profile("Seattle, WA");

        await UpdateAsync(profile, showtimesLocation: null);

        profile.ShowtimesLocation.Should().Be("Seattle, WA");
    }

    [Fact]
    public async Task An_empty_showtimes_location_clears_it()
    {
        var profile = Profile("Seattle, WA");

        await UpdateAsync(profile, showtimesLocation: "");

        profile.ShowtimesLocation.Should().BeNull();
    }

    [Fact]
    public async Task A_new_showtimes_location_is_trimmed_and_stored()
    {
        var profile = Profile(null);

        await UpdateAsync(profile, showtimesLocation: "  Dallas, TX  ");

        profile.ShowtimesLocation.Should().Be("Dallas, TX");
    }

    [Fact]
    public async Task The_dvr_permission_reaches_the_profile()
    {
        var profile = Profile(null);

        await UpdateAsync(profile, showtimesLocation: null, canRecordLiveTv: true);

        profile.CanRecordLiveTv.Should().BeTrue();
    }
}
