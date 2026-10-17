using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Vora.Application.Analysis;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Users;

public class LibrarySortPreferenceTests
{
    private readonly IUserRepository _repo = Substitute.For<IUserRepository>();
    private readonly UserManager _manager;
    private readonly UserProfile _profile = new() { Id = Guid.NewGuid(), Name = "Andy", UserId = Guid.NewGuid() };

    public LibrarySortPreferenceTests()
    {
        _manager = new UserManager(_repo, Substitute.For<IUserProfileImageService>(), Substitute.For<IClientNotifier>(), NullLogger<UserManager>.Instance);
        _repo.GetProfileByIdAsync(_profile.Id).Returns(_profile);
    }

    [Fact]
    public async Task A_new_profile_follows_the_server_default()
    {
        var sort = await _manager.GetLibrarySortAsync(_profile.Id);

        sort.Sort.Should().BeNull();
        sort.Descending.Should().BeFalse();
    }

    [Fact]
    public async Task Saves_the_sort_and_its_direction_to_the_profile()
    {
        var saved = await _manager.SaveLibrarySortAsync(_profile.Id, new LibrarySortVM { Sort = LibrarySortField.ReleaseDate, Descending = true });

        saved.Sort.Should().Be(LibrarySortField.ReleaseDate);
        saved.Descending.Should().BeTrue();
        _profile.LibrarySort.Should().Be(LibrarySortField.ReleaseDate);
        _profile.LibrarySortDescending.Should().BeTrue();
        await _repo.Received(1).UpdateProfileAsync(_profile);
    }

    [Fact]
    public async Task Going_back_to_the_server_default_drops_the_direction_too()
    {
        _profile.LibrarySort = LibrarySortField.DateAdded;
        _profile.LibrarySortDescending = true;

        var saved = await _manager.SaveLibrarySortAsync(_profile.Id, new LibrarySortVM { Sort = null, Descending = true });

        saved.Sort.Should().BeNull();
        saved.Descending.Should().BeFalse();
        _profile.LibrarySortDescending.Should().BeFalse();
    }

    [Fact]
    public async Task Refuses_a_sort_that_does_not_exist()
    {
        var save = () => _manager.SaveLibrarySortAsync(_profile.Id, new LibrarySortVM { Sort = (LibrarySortField)42 });

        await save.Should().ThrowAsync<ArgumentException>();
        await _repo.DidNotReceive().UpdateProfileAsync(Arg.Any<UserProfile>());
    }
}
