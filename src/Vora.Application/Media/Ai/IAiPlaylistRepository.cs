using Vora.Domain.Entities.Media;

namespace Vora.Application.Media.Ai;

public interface IAiPlaylistRepository
{
    // The vectors of the songs a profile has played most in the window, with how
    // often - the raw material for its taste.
    Task<List<PlayedVector>> GetPlayedVectorsAsync(Guid profileId, int withinDays, int limit);

    // The songs nearest the vector that the access filter allows, nearest
    // first. The only way AI playlists get songs, so the viewer's parental
    // controls apply to every one.
    Task<List<AiTrackCandidate>> FindNearestTracksAsync(float[] vector, MusicAccessFilter access, AiTrackFilter filter, int limit);

    Task<List<Guid>> GetProfilesDueForWeeklyAsync(DateTime generatedBefore, int minPlays, int withinDays);
    Task<List<BlendPartner>> GetBlendPartnersAsync(Guid profileId);
    Task<int> CountRequestsSinceAsync(Guid profileId, DateTime since);
    Task<List<GeneratedMix>> GetAiMixesAsync(Guid profileId);

    Task ReplaceWeeklyAsync(Guid profileId, IReadOnlyList<GeneratedMix> mixes);
    Task ReplaceBlendAsync(GeneratedMix blend);
    Task AddRequestAsync(GeneratedMix request, int keep);
}

public sealed record PlayedVector(float[] Vector, int Plays);

public sealed record AiTrackCandidate(Guid TrackId, string ArtistKey, string? ArtworkUrl);

public sealed record AiTrackFilter(int? YearFrom = null, int? YearTo = null, IReadOnlyCollection<Guid>? Exclude = null)
{
    public static AiTrackFilter None { get; } = new();
}

public sealed record BlendPartner(Guid ProfileId, string Name, string? ImageUrl);
