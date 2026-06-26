using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Users;

public class UserManagerTests
{
    private readonly IUserRepository _repo;
    private readonly IUserProfileImageService _images;
    private readonly IClientNotifier _notifier;
    private readonly UserManager _manager;

    public UserManagerTests()
    {
        _repo = Substitute.For<IUserRepository>();
        _images = Substitute.For<IUserProfileImageService>();
        _notifier = Substitute.For<IClientNotifier>();
        _manager = new UserManager(_repo, _images, _notifier, NullLogger<UserManager>.Instance);
    }

    private static UserProfile MakeProfile(Guid id, string? imageUrl = null) => new()
    {
        Id = id,
        Name = "Andy",
        UserId = Guid.NewGuid(),
        ProfileImageUrl = imageUrl
    };

    private static User MakeUser(Guid id, bool isAdmin = false) => new()
    {
        Id = id,
        Email = "a@b.com",
        DisplayName = "Andy",
        PasswordHash = "old-hash",
        SecurityStamp = "old-stamp",
        IsAdmin = isAdmin
    };

    // ---------- ValidateProfilePinAsync ----------

    [Fact]
    public async Task ValidateProfilePinAsync_returns_true_when_no_pin_set()
    {
        _repo.GetProjectedProfileByIdAsync(Arg.Any<Guid>(), Arg.Any<Expression<Func<UserProfile, string?>>>())
            .Returns((string?)null);

        (await _manager.ValidateProfilePinAsync(Guid.NewGuid(), "1234")).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateProfilePinAsync_returns_true_when_pin_hash_blank()
    {
        _repo.GetProjectedProfileByIdAsync(Arg.Any<Guid>(), Arg.Any<Expression<Func<UserProfile, string?>>>())
            .Returns("");

        (await _manager.ValidateProfilePinAsync(Guid.NewGuid(), "1234")).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateProfilePinAsync_returns_false_for_wrong_pin()
    {
        // The pin stored is the SHA-256 hex of "1234"; we'll compute it inline below.
        var correctHash = ComputeSha256Hex("1234");
        _repo.GetProjectedProfileByIdAsync(Arg.Any<Guid>(), Arg.Any<Expression<Func<UserProfile, string?>>>())
            .Returns(correctHash);

        (await _manager.ValidateProfilePinAsync(Guid.NewGuid(), "9999")).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateProfilePinAsync_returns_true_for_matching_pin()
    {
        var correctHash = ComputeSha256Hex("1234");
        _repo.GetProjectedProfileByIdAsync(Arg.Any<Guid>(), Arg.Any<Expression<Func<UserProfile, string?>>>())
            .Returns(correctHash);

        (await _manager.ValidateProfilePinAsync(Guid.NewGuid(), "1234")).Should().BeTrue();
    }

    // ---------- CreateManagedProfileAsync ----------

    [Fact]
    public async Task CreateManagedProfileAsync_persists_profile_and_returns_id()
    {
        var userId = Guid.NewGuid();

        var id = await _manager.CreateManagedProfileAsync(
            userId, "Kid", imageUrl: null, pin: null,
            allowedMovieRatings: new List<string> { "G" },
            allowedTvRatings: new List<string> { "TV-Y" },
            allowedMusicRatings: new List<string>(),
            hasAllLibraryAccess: false, blockUnrated: true,
            allowedLibraries: new List<Guid>(),
            hasAllIptvAccess: false, allowedIptvPlaylists: new List<Guid>(),
            schedules: new List<ProfileScheduleVM>(),
            canAddCustomPodcastFeeds: false,
            showtimesLocation: null);

        id.Should().NotBe(Guid.Empty);
        await _repo.Received(1).AddProfileAsync(Arg.Is<UserProfile>(p =>
            p.Name == "Kid" &&
            p.UserId == userId &&
            p.BlockUnratedContent &&
            !p.HasAllLibraryAccess &&
            !p.HasAllIptvAccess &&
            !p.CanAddCustomPodcastFeeds));
    }

    [Fact]
    public async Task CreateManagedProfileAsync_hashes_pin_when_provided()
    {
        UserProfile? captured = null;
        await _repo.AddProfileAsync(Arg.Do<UserProfile>(p => captured = p));

        await _manager.CreateManagedProfileAsync(
            Guid.NewGuid(), "Kid", null, pin: "1234",
            new List<string>(), new List<string>(), new List<string>(),
            true, false, new List<Guid>(), true, new List<Guid>(),
            new List<ProfileScheduleVM>(), true, null);

        captured.Should().NotBeNull();
        captured!.PinHash.Should().Be(ComputeSha256Hex("1234"));
    }

    [Fact]
    public async Task CreateManagedProfileAsync_leaves_pin_hash_null_when_pin_blank()
    {
        UserProfile? captured = null;
        await _repo.AddProfileAsync(Arg.Do<UserProfile>(p => captured = p));

        await _manager.CreateManagedProfileAsync(
            Guid.NewGuid(), "Kid", null, pin: "   ",
            new List<string>(), new List<string>(), new List<string>(),
            true, false, new List<Guid>(), true, new List<Guid>(),
            new List<ProfileScheduleVM>(), true, null);

        captured!.PinHash.Should().BeNull();
    }

    [Fact]
    public async Task CreateManagedProfileAsync_trims_showtimes_location()
    {
        UserProfile? captured = null;
        await _repo.AddProfileAsync(Arg.Do<UserProfile>(p => captured = p));

        await _manager.CreateManagedProfileAsync(
            Guid.NewGuid(), "Andy", null, null,
            new List<string>(), new List<string>(), new List<string>(),
            true, false, new List<Guid>(), true, new List<Guid>(),
            new List<ProfileScheduleVM>(), true,
            showtimesLocation: "  Seattle, WA  ");

        captured!.ShowtimesLocation.Should().Be("Seattle, WA");
    }

    [Fact]
    public async Task CreateManagedProfileAsync_normalizes_blank_showtimes_to_null()
    {
        UserProfile? captured = null;
        await _repo.AddProfileAsync(Arg.Do<UserProfile>(p => captured = p));

        await _manager.CreateManagedProfileAsync(
            Guid.NewGuid(), "Andy", null, null,
            new List<string>(), new List<string>(), new List<string>(),
            true, false, new List<Guid>(), true, new List<Guid>(),
            new List<ProfileScheduleVM>(), true,
            showtimesLocation: "   ");

        captured!.ShowtimesLocation.Should().BeNull();
    }

    // ---------- UpdateUserAccountAsync ----------

    [Fact]
    public async Task UpdateUserAccountAsync_throws_unauthorized_when_non_admin_updates_other_account()
    {
        var act = async () => await _manager.UpdateUserAccountAsync(
            userId: Guid.NewGuid(),
            callingAccountId: Guid.NewGuid(),
            callerIsAdmin: false,
            displayName: "X", newPassword: null);

        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*your own account*");
    }

    [Fact]
    public async Task UpdateUserAccountAsync_allows_self_update_for_non_admin()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        _repo.GetUserByIdAsync(userId).Returns(user);

        await _manager.UpdateUserAccountAsync(
            userId, callingAccountId: userId, callerIsAdmin: false,
            displayName: "New Name", newPassword: null);

        user.DisplayName.Should().Be("New Name");
        await _repo.Received(1).UpdateUserAsync(user);
    }

    [Fact]
    public async Task UpdateUserAccountAsync_allows_admin_to_update_other_account()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        _repo.GetUserByIdAsync(userId).Returns(user);

        await _manager.UpdateUserAccountAsync(
            userId, callingAccountId: Guid.NewGuid(), callerIsAdmin: true,
            displayName: "X", newPassword: null);

        await _repo.Received(1).UpdateUserAsync(user);
    }

    [Fact]
    public async Task UpdateUserAccountAsync_throws_when_user_not_found()
    {
        var userId = Guid.NewGuid();
        _repo.GetUserByIdAsync(userId).Returns((User?)null);

        var act = async () => await _manager.UpdateUserAccountAsync(
            userId, callingAccountId: userId, callerIsAdmin: false,
            displayName: "X", newPassword: null);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*User not found*");
    }

    [Fact]
    public async Task UpdateUserAccountAsync_rotates_security_stamp_only_when_password_changes()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        var originalStamp = user.SecurityStamp;
        _repo.GetUserByIdAsync(userId).Returns(user);

        await _manager.UpdateUserAccountAsync(
            userId, userId, false, "X", newPassword: null);

        user.SecurityStamp.Should().Be(originalStamp);
        user.PasswordHash.Should().Be("old-hash");
    }

    [Fact]
    public async Task UpdateUserAccountAsync_hashes_new_password_and_rotates_security_stamp()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        var originalStamp = user.SecurityStamp;
        _repo.GetUserByIdAsync(userId).Returns(user);

        await _manager.UpdateUserAccountAsync(
            userId, userId, false, "X", newPassword: "new-secret");

        user.PasswordHash.Should().NotBe("old-hash");
        BCrypt.Net.BCrypt.Verify("new-secret", user.PasswordHash).Should().BeTrue();
        user.SecurityStamp.Should().NotBe(originalStamp);
    }

    [Fact]
    public async Task UpdateUserAccountAsync_does_not_touch_email_notify_flag_when_request_omits_it()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        user.EmailNotifyOnRequestAvailable = true;
        _repo.GetUserByIdAsync(userId).Returns(user);

        await _manager.UpdateUserAccountAsync(
            userId, userId, false, "X", newPassword: null,
            emailNotifyOnRequestAvailable: null);

        user.EmailNotifyOnRequestAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateUserAccountAsync_writes_email_notify_flag_when_request_provides_it()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        user.EmailNotifyOnRequestAvailable = true;
        _repo.GetUserByIdAsync(userId).Returns(user);

        await _manager.UpdateUserAccountAsync(
            userId, userId, false, "X", newPassword: null,
            emailNotifyOnRequestAvailable: false);

        user.EmailNotifyOnRequestAvailable.Should().BeFalse();
    }

    // ---------- DeleteManagedProfileAsync ----------

    [Fact]
    public async Task DeleteManagedProfileAsync_no_op_on_repo_when_profile_missing_but_still_notifies()
    {
        var profileId = Guid.NewGuid();
        _repo.GetProfileByIdAsync(profileId).Returns((UserProfile?)null);

        await _manager.DeleteManagedProfileAsync(profileId);

        await _repo.DidNotReceive().DeleteProfileAsync(Arg.Any<Guid>());
        await _notifier.Received(1).NotifyProfileAccessUpdatedAsync(profileId);
    }

    [Fact]
    public async Task DeleteManagedProfileAsync_removes_profile_image_then_deletes_and_notifies()
    {
        var profileId = Guid.NewGuid();
        var profile = MakeProfile(profileId, imageUrl: "/img/p.jpg");
        _repo.GetProfileByIdAsync(profileId).Returns(profile);

        await _manager.DeleteManagedProfileAsync(profileId);

        _images.Received(1).DeleteImage("/img/p.jpg");
        await _repo.Received(1).DeleteProfileAsync(profileId);
        await _notifier.Received(1).NotifyProfileAccessUpdatedAsync(profileId);
    }

    // ---------- UpdateUserAccessAsync ----------

    [Fact]
    public async Task UpdateUserAccessAsync_throws_when_user_not_found()
    {
        _repo.GetUserByIdAsync(Arg.Any<Guid>()).Returns((User?)null);

        var act = async () => await _manager.UpdateUserAccessAsync(
            Guid.NewGuid(), false, new List<Guid>(), false, false, false,
            false, new List<Guid>(), false, 0, false, false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*User not found*");
    }

    [Fact]
    public async Task UpdateUserAccessAsync_forces_full_access_on_admin_users()
    {
        var userId = Guid.NewGuid();
        var admin = MakeUser(userId, isAdmin: true);
        admin.HasAllLibraryAccess = false;
        admin.AllowedLibraryIds = new List<Guid> { Guid.NewGuid() };
        admin.HasAllIptvAccess = false;
        admin.AllowedIptvPlaylistIds = new List<Guid> { Guid.NewGuid() };
        _repo.GetUserByIdAsync(userId).Returns(admin);

        await _manager.UpdateUserAccessAsync(
            userId,
            hasAllLibraryAccess: false,
            allowedLibraries: new List<Guid> { Guid.NewGuid() },
            canRequest: true, autoApprove: true, enableAiRecommendations: true,
            hasAllIptvAccess: false,
            allowedIptvPlaylists: new List<Guid> { Guid.NewGuid() },
            canRecordLiveTv: false, dvrStorageQuotaBytes: 0,
            canTimeshiftIptv: false, canAddCustomPodcastFeeds: false);

        admin.HasAllLibraryAccess.Should().BeTrue();
        admin.AllowedLibraryIds.Should().BeEmpty();
        admin.HasAllIptvAccess.Should().BeTrue();
        admin.AllowedIptvPlaylistIds.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateUserAccessAsync_applies_partial_access_for_non_admin_users()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);
        _repo.GetUserByIdAsync(userId).Returns(user);
        var libIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var iptvIds = new List<Guid> { Guid.NewGuid() };

        await _manager.UpdateUserAccessAsync(
            userId,
            hasAllLibraryAccess: false, allowedLibraries: libIds,
            canRequest: true, autoApprove: false, enableAiRecommendations: true,
            hasAllIptvAccess: false, allowedIptvPlaylists: iptvIds,
            canRecordLiveTv: true, dvrStorageQuotaBytes: 100,
            canTimeshiftIptv: true, canAddCustomPodcastFeeds: true);

        user.HasAllLibraryAccess.Should().BeFalse();
        user.AllowedLibraryIds.Should().BeEquivalentTo(libIds);
        user.HasAllIptvAccess.Should().BeFalse();
        user.AllowedIptvPlaylistIds.Should().BeEquivalentTo(iptvIds);
    }

    private static string ComputeSha256Hex(string input)
    {
        // UserManager.HashPin returns Base64 of SHA-256 — name kept for brevity.
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }
}
