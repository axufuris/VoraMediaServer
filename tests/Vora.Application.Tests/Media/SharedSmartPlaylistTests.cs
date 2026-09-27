using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Media;

// A shared smart playlist shares its RULES, and the profile it is evaluated for
// does two jobs that sharing splits apart. Rules like "tracks I've played ten
// times" describe the OWNER's taste, so they read the owner's plays; what may be
// SHOWN is the viewer's business, so visibility comes from the viewer's
// parental controls. Get that backwards and opening Andy's "Most Played" shows
// the viewer their own most played under Andy's name.
public class SharedSmartPlaylistTests
{
    private readonly ISmartPlaylistRepository _repo = Substitute.For<ISmartPlaylistRepository>();
    private readonly ISmartPlaylistEvaluator _evaluator = Substitute.For<ISmartPlaylistEvaluator>();
    private readonly IMusicRepository _music = Substitute.For<IMusicRepository>();
    private readonly SmartPlaylistManager _manager;

    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _viewer = Guid.NewGuid();

    private static readonly PlaylistAccessFilter ViewersControls = new() { AllowedMusicRatings = new List<string> { "Clean" } };

    public SharedSmartPlaylistTests()
    {
        _manager = new SmartPlaylistManager(_repo, _evaluator, _music);
        _music.GetLikedTrackIdsAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<Guid>>()).Returns(new HashSet<Guid>());
    }

    private SmartPlaylist SharedByOwner(string name = "Most Played")
    {
        var row = new SmartPlaylist
        {
            Id = Guid.NewGuid(),
            ProfileId = _owner,
            Profile = new UserProfile { Id = _owner, Name = "Andy" },
            Name = name,
            MediaType = PlaylistMediaType.Music,
            RulesJson = """{"match":"All","rules":[]}""",
            SortBy = "Title",
            IsShared = true,
            SharedAt = DateTime.UtcNow
        };
        _repo.GetVisibleAsync(row.Id, Arg.Any<Guid>()).Returns(row);
        return row;
    }

    [Fact]
    public async Task A_shared_playlist_is_evaluated_on_the_owners_taste_with_the_viewers_controls()
    {
        var row = SharedByOwner();
        _evaluator.EvaluateAsync(Arg.Any<SmartPlaylistDefinition>(), Arg.Any<PlaylistMediaType>(), Arg.Any<Guid>(), Arg.Any<PlaylistAccessFilter>())
            .Returns(new List<MediaItem>());

        await _manager.GetItemsAsync(row.Id, _viewer, ViewersControls);

        await _evaluator.Received(1).EvaluateAsync(
            Arg.Any<SmartPlaylistDefinition>(), PlaylistMediaType.Music, _owner, ViewersControls);
    }

    // The hearts on each row belong to whoever is looking at them.
    [Fact]
    public async Task The_likes_shown_are_the_viewers_own()
    {
        var row = SharedByOwner();
        var track = new Track { Id = Guid.NewGuid(), Title = "Lucky" };
        _evaluator.EvaluateAsync(Arg.Any<SmartPlaylistDefinition>(), Arg.Any<PlaylistMediaType>(), Arg.Any<Guid>(), Arg.Any<PlaylistAccessFilter>())
            .Returns(new List<MediaItem> { track });

        await _manager.GetItemsAsync(row.Id, _viewer, ViewersControls);

        await _music.Received(1).GetLikedTrackIdsAsync(_viewer, Arg.Any<IEnumerable<Guid>>());
    }

    [Fact]
    public async Task A_viewer_is_told_whose_it_is_and_that_it_is_not_theirs()
    {
        var row = SharedByOwner();

        var detail = await _manager.GetAsync(row.Id, _viewer, ViewersControls);

        detail!.IsOwner.Should().BeFalse();
        detail.OwnerName.Should().Be("Andy");
        detail.IsShared.Should().BeTrue();
    }

    // Updates still load through the owner-only GetByIdAsync, so sharing never
    // hands out write access to the rules.
    [Fact]
    public async Task A_viewer_cannot_rewrite_the_rules()
    {
        var row = SharedByOwner();
        _repo.GetByIdAsync(row.Id, _viewer).Returns((SmartPlaylist?)null);

        var result = await _manager.UpdateAsync(row.Id, _viewer, new SmartPlaylistSaveRequest { Name = "Hijacked", MediaType = PlaylistMediaType.Music });

        result.Should().BeNull();
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<SmartPlaylist>());
    }

    [Fact]
    public async Task A_copy_takes_the_rules_is_the_viewers_and_starts_unshared()
    {
        var row = SharedByOwner();
        SmartPlaylist? saved = null;
        await _repo.AddAsync(Arg.Do<SmartPlaylist>(p => saved = p));

        var copyId = await _manager.CopyAsync(row.Id, _viewer);

        copyId.Should().NotBeNull();
        saved!.ProfileId.Should().Be(_viewer);
        saved.IsShared.Should().BeFalse("otherwise saving it adds a duplicate to everyone's Shared tab");
        saved.RulesJson.Should().Be(row.RulesJson);
        saved.Name.Should().Be(row.Name);
    }

    [Fact]
    public async Task An_unshared_one_belonging_to_someone_else_cannot_be_copied()
    {
        _repo.GetVisibleAsync(Arg.Any<Guid>(), _viewer).Returns((SmartPlaylist?)null);

        var copyId = await _manager.CopyAsync(Guid.NewGuid(), _viewer);

        copyId.Should().BeNull();
        await _repo.DidNotReceive().AddAsync(Arg.Any<SmartPlaylist>());
    }

    // Counted on the owner's taste with the viewer's controls, and left out when
    // it would show the viewer nothing — its title alone can be what a parent's
    // controls exist to keep from a child.
    [Fact]
    public async Task The_shared_tab_leaves_out_what_would_show_the_viewer_nothing()
    {
        var visible = SharedByOwner("Clean Hits");
        var empty = SharedByOwner("Explicit Only");
        _repo.GetSharedByOthersAsync(_viewer).Returns(new List<SmartPlaylist> { visible, empty });
        // Called in list order: three visible tracks, then none.
        _evaluator.CountAsync(Arg.Any<SmartPlaylistDefinition>(), Arg.Any<PlaylistMediaType>(), _owner, ViewersControls)
            .Returns(3, 0);

        var shared = await _manager.GetSharedByOthersAsync(_viewer, ViewersControls);

        shared.Select(p => p.Name).Should().Equal("Clean Hits");
        shared.Single().OwnerName.Should().Be("Andy");
        shared.Single().TrackCount.Should().Be(3);
    }
}
