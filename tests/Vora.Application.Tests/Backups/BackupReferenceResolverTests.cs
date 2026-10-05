using Vora.Application.Backups;

namespace Vora.Application.Tests.Backups;

public class BackupReferenceResolverTests
{
    private static readonly Guid Original = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Rescanned = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid OtherLibraryCopy = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private static Dictionary<BackupItemKind, HashSet<Guid>> Present(params Guid[] ids) =>
        new() { [BackupItemKind.MediaItem] = ids.ToHashSet() };

    private static BackupIdentityFile Recorded(Guid id, string? library, params string[] keys) =>
        new() { MediaItems = { new BackupItemIdentity { Id = id, Keys = keys.ToList(), Library = library } } };

    [Fact]
    public void An_item_that_still_exists_keeps_its_id()
    {
        var resolver = new BackupReferenceResolver(Present(Original), Recorded(Original, null, "movie:tmdb:603"), new BackupIdentityFile());

        resolver.Resolve(BackupItemKind.MediaItem, Original).Should().Be(Original);
        resolver.RemappedCount.Should().Be(0);
    }

    [Fact]
    public void A_missing_item_is_matched_to_this_servers_copy_by_its_identity()
    {
        var current = new BackupIdentityFile { MediaItems = { new BackupItemIdentity { Id = Rescanned, Keys = { "movie:tmdb:603" } } } };
        var resolver = new BackupReferenceResolver(Present(), Recorded(Original, null, "movie:tmdb:603"), current);

        resolver.Resolve(BackupItemKind.MediaItem, Original).Should().Be(Rescanned);
        resolver.RemappedCount.Should().Be(1);
    }

    [Fact]
    public void Stronger_keys_are_tried_before_weaker_ones()
    {
        var current = new BackupIdentityFile
        {
            MediaItems =
            {
                new BackupItemIdentity { Id = OtherLibraryCopy, Keys = { "movie:title:the matrix|1999" } },
                new BackupItemIdentity { Id = Rescanned, Keys = { "movie:imdb:tt0133093" } }
            }
        };
        var resolver = new BackupReferenceResolver(
            Present(),
            Recorded(Original, null, "movie:tmdb:603", "movie:imdb:tt0133093", "movie:title:the matrix|1999"),
            current);

        resolver.Resolve(BackupItemKind.MediaItem, Original).Should().Be(Rescanned);
    }

    [Fact]
    public void The_copy_in_the_library_with_the_same_name_wins_a_tie()
    {
        var current = new BackupIdentityFile
        {
            MediaItems =
            {
                new BackupItemIdentity { Id = Rescanned, Keys = { "movie:tmdb:603" }, Library = "Movies" },
                new BackupItemIdentity { Id = OtherLibraryCopy, Keys = { "movie:tmdb:603" }, Library = "Movies 4K" }
            }
        };
        var resolver = new BackupReferenceResolver(Present(), Recorded(Original, "movies 4k", "movie:tmdb:603"), current);

        resolver.Resolve(BackupItemKind.MediaItem, Original).Should().Be(OtherLibraryCopy);
    }

    [Fact]
    public void An_item_with_no_match_is_unresolved()
    {
        var current = new BackupIdentityFile { MediaItems = { new BackupItemIdentity { Id = Rescanned, Keys = { "movie:tmdb:999" } } } };
        var resolver = new BackupReferenceResolver(Present(), Recorded(Original, null, "movie:tmdb:603"), current);

        resolver.Resolve(BackupItemKind.MediaItem, Original).Should().BeNull();
        resolver.Resolve(BackupItemKind.MediaItem, (Guid?)null).Should().BeNull();
    }

    [Fact]
    public void A_backup_without_identities_only_keeps_items_that_still_exist()
    {
        var resolver = new BackupReferenceResolver(Present(Original), null, new BackupIdentityFile());

        resolver.Resolve(BackupItemKind.MediaItem, Original).Should().Be(Original);
        resolver.Resolve(BackupItemKind.MediaItem, Rescanned).Should().BeNull();
    }

    [Fact]
    public void Kinds_do_not_match_each_other()
    {
        var current = new BackupIdentityFile { Albums = { new BackupItemIdentity { Id = Rescanned, Keys = { "shared-key" } } } };
        var resolver = new BackupReferenceResolver(Present(), Recorded(Original, null, "shared-key"), current);

        resolver.Resolve(BackupItemKind.MediaItem, Original).Should().BeNull();
    }
}
