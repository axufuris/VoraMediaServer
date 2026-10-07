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
    Task<List<AiTrackCandidate>> GetTrackArtAsync(IReadOnlyCollection<Guid> trackIds);
    Task<List<TrackForOrdering>> GetTracksForOrderingAsync(IReadOnlyCollection<Guid> trackIds);

    Task<List<Guid>> GetProfilesDueForWeeklyAsync(DateTime generatedBefore, int minPlays, int withinDays);
    Task<List<BlendPartner>> GetBlendPartnersAsync(Guid profileId);
    Task<int> CountRequestsSinceAsync(Guid profileId, DateTime since);
    Task<GeneratedMix?> GetAiMixAsync(Guid profileId, Guid mixId);
    Task<List<GeneratedMix>> GetAiMixesAsync(Guid profileId);

    Task ReplaceWeeklyAsync(Guid profileId, IReadOnlyList<GeneratedMix> mixes);
    Task ReplaceBlendAsync(GeneratedMix blend);
    Task AddRequestAsync(GeneratedMix request, int keep);
    Task RebuildRequestAsync(Guid mixId, GeneratedMix rebuilt);
    Task<bool> DeleteAiMixAsync(Guid profileId, Guid mixId, IReadOnlyCollection<GeneratedMixKind> kinds);
}

public sealed record PlayedVector(float[] Vector, int Plays);

public sealed record TrackForOrdering(Guid TrackId, string Title, string? Artist, Vora.Domain.Enums.TrackEnergy? Energy, List<string>? Moods);

public sealed record AiTrackCandidate(
    Guid TrackId,
    string ArtistKey,
    string? ArtworkUrl,
    double Distance = 0,
    string? Title = null,
    int? DurationSeconds = null,
    string? AudioCodec = null,
    int? SampleRate = null,
    int? Bitrate = null)
{
    public SongFacts Song => new(ArtistKey, Title, DurationSeconds, AudioCodec, SampleRate, Bitrate);
}

public sealed record AiTrackFilter(int? YearFrom = null, int? YearTo = null, IReadOnlyCollection<Guid>? Exclude = null)
{
    public static AiTrackFilter None { get; } = new();
}

public sealed record BlendPartner(Guid ProfileId, string Name, string? ImageUrl);
