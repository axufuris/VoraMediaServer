using Vora.Application.Media;

namespace Vora.Application.SmartLists;

public sealed record SmartListViewer(
    Guid? AccountId,
    Guid? ProfileId,
    bool IsAdmin,
    bool HasAllLibraryAccess,
    List<Guid> AllowedLibraryIds,
    List<string> AllowedMovieRatings,
    List<string> AllowedTvRatings,
    bool BlockUnrated,
    MusicAccessFilter MusicAccess);
