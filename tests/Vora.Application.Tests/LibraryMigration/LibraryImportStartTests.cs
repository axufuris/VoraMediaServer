using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.LibraryMigration;
using Vora.Application.LibraryMigration.Requests;
using Vora.Application.LibraryMigration.ViewModels;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.LibraryMigration;

public class LibraryImportStartTests
{
    private readonly Guid _account = Guid.NewGuid();
    private readonly UserProfile _main;
    private readonly UserProfile _kid;
    private readonly UserProfile _neighbour;
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ILibraryMigrationJobRunner _runner = Substitute.For<ILibraryMigrationJobRunner>();
    private readonly LibraryMigrationManager _manager;
    private LibraryMigrationJobInput? _started;

    public LibraryImportStartTests()
    {
        _main = new UserProfile { Id = Guid.NewGuid(), Name = "Andy", UserId = _account, IsAdmin = true };
        _kid = new UserProfile { Id = Guid.NewGuid(), Name = "Kids", UserId = _account };
        _neighbour = new UserProfile { Id = Guid.NewGuid(), Name = "Neighbour", UserId = Guid.NewGuid(), IsAdmin = true };
        foreach (var profile in new[] { _main, _kid, _neighbour }) _users.GetProfileByIdAsync(profile.Id).Returns(profile);

        var plex = Substitute.For<ILibrarySyncProvider>();
        plex.Id.Returns("plex");
        _runner.StartJob(Arg.Do<LibraryMigrationJobInput>(input => _started = input)).Returns(new LibraryMigrationJobVM { JobId = Guid.NewGuid(), ProviderId = "plex", ServerName = "Home", State = LibraryMigrationJobState.Pending, Users = [] });
        _manager = new LibraryMigrationManager([plex], _users, _runner, NullLogger<LibraryMigrationManager>.Instance);
    }

    private static StartLibraryImportRequest Request(Action<StartLibraryImportRequest>? change = null)
    {
        var request = new StartLibraryImportRequest { AccessToken = "plex-token", ServerClientIdentifier = "server", ServerName = "Home", LibrarySectionKeys = ["1"], PlexUsername = "andyx" };
        change?.Invoke(request);
        return request;
    }

    [Fact]
    public async Task A_profile_imports_its_own_Plex_history_into_itself()
    {
        await _manager.StartImportAsync("plex", Request(), _account, _kid.Id, isServerAdmin: false);

        var mapping = _started!.Mappings.Single();
        (mapping.AccountId, mapping.AccountName, mapping.ProfileId, mapping.ProfileName).Should().Be((LibraryMigrationMappingInput.SignedInAccount, "andyx", _kid.Id, "Kids"));
        _started.RequestedByAccountId.Should().Be(_account);
    }

    [Fact]
    public async Task The_main_profile_imports_a_Plex_Home_users_history_into_another_profile_on_the_account()
    {
        await _manager.StartImportAsync("plex", Request(r => { r.PlexAccountId = "kid-uuid"; r.PlexAccountName = "Kid"; r.Pin = "1234"; r.ProfileId = _kid.Id; }), _account, _main.Id, isServerAdmin: false);

        var mapping = _started!.Mappings.Single();
        (mapping.AccountId, mapping.AccountName, mapping.Pin, mapping.ProfileId, mapping.ProfileName).Should().Be(("kid-uuid", "Kid", "1234", _kid.Id, "Kids"));
    }

    [Fact]
    public async Task Another_profile_cannot_import_into_a_profile_other_than_itself()
    {
        var start = () => _manager.StartImportAsync("plex", Request(r => r.ProfileId = _main.Id), _account, _kid.Id, isServerAdmin: false);

        await start.Should().ThrowAsync<UnauthorizedAccessException>();
        _runner.DidNotReceiveWithAnyArgs().StartJob(default!);
    }

    [Fact]
    public async Task Nobody_can_import_into_a_profile_on_another_account()
    {
        var start = () => _manager.StartImportAsync("plex", Request(r => r.ProfileId = _neighbour.Id), _account, _main.Id, isServerAdmin: true);

        await start.Should().ThrowAsync<UnauthorizedAccessException>();
        _runner.DidNotReceiveWithAnyArgs().StartJob(default!);
    }

    [Fact]
    public async Task Server_ratings_come_only_from_a_server_admin_importing_their_own_history_into_their_own_profile()
    {
        await _manager.StartImportAsync("plex", Request(r => r.SetAdminRatings = true), _account, _main.Id, isServerAdmin: true);
        _started!.SetAdminRatings.Should().BeTrue();

        await _manager.StartImportAsync("plex", Request(r => { r.SetAdminRatings = true; r.PlexAccountId = "kid-uuid"; r.ProfileId = _kid.Id; }), _account, _main.Id, isServerAdmin: true);
        _started!.SetAdminRatings.Should().BeFalse();

        await _manager.StartImportAsync("plex", Request(r => r.SetAdminRatings = true), _account, _main.Id, isServerAdmin: false);
        _started!.SetAdminRatings.Should().BeFalse();
    }

    [Fact]
    public async Task An_import_with_nothing_picked_is_refused()
    {
        var start = () => _manager.StartImportAsync("plex", Request(r => { r.IncludeWatchState = false; r.IncludeRatings = false; }), _account, _main.Id, isServerAdmin: false);

        await start.Should().ThrowAsync<ArgumentException>();
    }
}
