using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Library;

// What a library's contents can support, for the subsystems that only apply to
// some kinds of library. The MediaItem-level counterpart is MediaCapabilities.
//
// This rule was previously a static on VideoThumbnailManager, and three of its
// four callers reached across for it by full name — LibraryManager, the
// scheduled job worker and subtitle pre-extraction all asking a thumbnail class
// whether a library holds video. Naming it for one of its consumers is how a
// rule ends up recopied by the next consumer that does not think to look there.
public static class LibraryCapabilities
{
    // Holds files with a video stream, so frames, embedded subtitle tracks and
    // scrub-bar sprites all mean something. Music has none of those, and Live TV
    // is not scanned into media items at all.
    public static bool HasVideoContent(this LibraryType type) =>
        type is LibraryType.Movie or LibraryType.TvShow or LibraryType.HomeVideo;
}
