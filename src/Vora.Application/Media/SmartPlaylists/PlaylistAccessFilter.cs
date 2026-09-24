namespace Vora.Application.Media.SmartPlaylists;

// What a profile may see in a playlist, which can hold films, episodes or tracks.
//
// Smart playlists used to be evaluated with a MusicAccessFilter for every kind,
// so a playlist of films was filtered against the MUSIC allowlist — "Clean" and
// "Explicit" — rather than the film one. It only failed safe by accident. Making
// music follow its own allowlist would have turned that accident into R-rated
// films in a child's smart playlist, so each kind now gets its own rule.
public class PlaylistAccessFilter
{
    public bool HasAllLibraryAccess { get; init; } = true;
    public List<Guid> AllowedLibraryIds { get; init; } = new();

    // Films and episodes are filtered exactly the way browsing filters them, so a
    // smart playlist shows a child precisely what their library shows them. Each
    // kind follows its own allowlist, and an empty one leaves that kind open.
    public List<string> AllowedMovieRatings { get; init; } = new();
    public List<string> AllowedTvRatings { get; init; } = new();

    public List<string> AllowedMusicRatings { get; init; } = new();

    public bool BlockUnratedContent { get; init; }

    public MusicAccessFilter Music => new()
    {
        HasAllLibraryAccess = HasAllLibraryAccess,
        AllowedLibraryIds = AllowedLibraryIds,
        AllowedRatings = AllowedMusicRatings,
        BlockUnratedContent = BlockUnratedContent,
    };

    public static PlaylistAccessFilter Unrestricted => new();
}
