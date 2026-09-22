using System.Reflection;
using Vora.Application.Libraries;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Libraries;

// The registry only earns its keep if it cannot go stale, so these assert it
// against the domain itself. Add an entity with a LibraryId and the build fails
// until it is registered — which is the moment to go and check the library-wide
// operations, because the bug it guards against is a quiet one: a sweep written
// against MediaItems covers movies, shows, seasons, episodes and tracks, misses
// albums and artists, and reports success.
public class LibraryScopeTests
{
    private static PropertyInfo? LibraryIdOf(Type type) =>
        type.GetProperty("LibraryId", BindingFlags.Public | BindingFlags.Instance);

    private static IEnumerable<Type> DomainTypesWithLibraryId() =>
        typeof(MediaItem).Assembly
            .GetTypes()
            .Where(t => t.IsClass)
            .Where(t => LibraryIdOf(t)?.DeclaringType == t);

    private static bool IsRequired(Type type) =>
        LibraryIdOf(type)?.PropertyType == typeof(Guid);

    [Fact]
    public void Registers_every_entity_that_belongs_to_a_library()
    {
        var required = DomainTypesWithLibraryId().Where(IsRequired).ToList();

        required.Should().NotBeEmpty("the reflection query itself has to keep working");
        LibraryScope.ContentTypes.Should().BeEquivalentTo(
            required,
            "a non-nullable LibraryId means the entity IS library content, so every library-wide content operation has to cover it");
    }

    [Fact]
    public void Registers_every_entity_that_merely_points_at_a_library()
    {
        var optional = DomainTypesWithLibraryId().Where(t => !IsRequired(t)).ToList();

        optional.Should().NotBeEmpty();
        LibraryScope.ReferencingTypes.Should().BeEquivalentTo(
            optional,
            "a nullable LibraryId means the entity outlives the library, so deleting one must leave it pointing somewhere sane");
    }

    // The split is the point: content gets scanned and enriched, references do
    // not. Registering one as the other would send a sweep at the wrong set.
    [Fact]
    public void Keeps_content_and_reference_registrations_apart()
    {
        LibraryScope.ContentTypes.Should().OnlyContain(t => IsRequired(t));
        LibraryScope.ReferencingTypes.Should().OnlyContain(t => !IsRequired(t));
        LibraryScope.ContentTypes.Should().NotIntersectWith(LibraryScope.ReferencingTypes);
    }

    // MediaItem is the TPH root, so it stands in for its five subclasses. Listing
    // those separately would make the registry look complete while telling a
    // caller to query the same table five times.
    [Fact]
    public void Registers_the_media_item_root_rather_than_its_subclasses()
    {
        LibraryScope.ContentTypes.Should().Contain(typeof(MediaItem));
        LibraryScope.ContentTypes
            .Where(t => t != typeof(MediaItem))
            .Should().NotContain(t => t.IsSubclassOf(typeof(MediaItem)));
    }

    [Fact]
    public void Exposes_everything_it_knows_about()
    {
        LibraryScope.AllTypes.Should().BeEquivalentTo(DomainTypesWithLibraryId());
        LibraryScope.ContentTypeNames.Should().BeEquivalentTo(LibraryScope.ContentTypes.Select(t => t.Name));
    }
}
