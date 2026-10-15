namespace Vora.Plugins.Dtos;

public sealed record MovieFilePlacement(string FilePath, Guid MovieId, string? TmdbId, string? ImdbId, bool MatchedByHand);
