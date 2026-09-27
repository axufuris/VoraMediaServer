using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.SmartLists;

namespace Vora.Application.Libraries;

// Everything a library touches, named in one place, split by how it is attached.
//
// These do not share a base type. MediaItem is the TPH root for movies, shows,
// seasons, episodes and tracks; Album and Artist are LockableEntity instead. So
// the natural way to write a library-wide sweep — query MediaItems by LibraryId —
// silently covers five of the seven kinds, misses albums and artists, and reports
// success. That is exactly how a library metadata refresh came to do nothing at
// all for artist and album artwork.
//
// LibraryScopeTests asserts both lists against the domain by reflection, so
// adding an entity with a LibraryId fails the build until it is registered here.
// That failure is the prompt to go and look at the sweeps.
public static class LibraryScope
{
    // Required LibraryId: the entity IS part of a library and cannot exist
    // without one. A library-wide content operation — scan, enrich, refresh
    // artwork, delete — has to account for every one of these.
    public static IReadOnlyList<Type> ContentTypes { get; } = new[]
    {
        typeof(MediaItem),
        typeof(Album),
        typeof(Artist),
    };

    // Optional LibraryId: the entity exists in its own right and may POINT at a
    // library. These are not content to scan or enrich, but a library going away
    // still has to leave them in a sane state rather than pointing at nothing.
    public static IReadOnlyList<Type> ReferencingTypes { get; } = new[]
    {
        typeof(Collection),
        typeof(SmartList),
        typeof(MediaDedupeSettings),
    };

    public static IEnumerable<Type> AllTypes => ContentTypes.Concat(ReferencingTypes);

    public static IReadOnlyList<string> ContentTypeNames { get; } =
        ContentTypes.Select(t => t.Name).ToList();
}
