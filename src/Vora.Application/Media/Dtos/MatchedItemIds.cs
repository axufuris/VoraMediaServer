namespace Vora.Application.Media.Dtos;

public sealed record MatchedItemIds(Guid Id, string? TmdbId, string? ImdbId, string? TvdbId);
