using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

// The only two ratings the music filter understands. A profile's music allowlist
// is Clean and/or Explicit, so a hand-typed "PG" would be a rating no allowlist
// can permit: the track would vanish for every restricted profile, for no
// reason anyone could see. Hand edits are held to these two, or none.
public static class MusicContentRating
{
    public const string Explicit = "Explicit";
    public const string Clean = "Clean";

    public static bool TryNormalize(string? input, out string? rating)
    {
        rating = null;
        if (string.IsNullOrWhiteSpace(input)) return true;

        var value = input.Trim();
        if (value.Equals(Explicit, StringComparison.OrdinalIgnoreCase)) { rating = Explicit; return true; }
        if (value.Equals(Clean, StringComparison.OrdinalIgnoreCase)) { rating = Clean; return true; }
        return false;
    }

    public static MusicContentRatingSource SourceOf(Track track)
    {
        if (track.IsLocked(nameof(Track.ContentRating))) return MusicContentRatingSource.Manual;
        if (track.ContentRating == null) return MusicContentRatingSource.None;
        return track.ContentRatingProvider != null ? MusicContentRatingSource.Provider : MusicContentRatingSource.FileTag;
    }

    // A hand edit is locked, so a rescan's file tag and the provider lookup both
    // leave it alone - a parent who corrects a song expects it to stay corrected.
    // Clearing to none is locked too, or the provider would fill it straight back.
    public static void SetByHand(Track track, string? rating)
    {
        track.ContentRating = rating;
        track.ContentRatingProvider = null;
        track.LockField(nameof(Track.ContentRating));
    }
}

public enum MusicContentRatingSource
{
    None = 0,
    FileTag = 1,
    Provider = 2,
    Manual = 3,
}
