using Vora.Plugins.Dtos;

namespace Vora.Application.Metadata.Local;

public static class LocalMetadataMerge
{
    public static MetadataResult Overlay(MetadataResult provider, MetadataResult document)
    {
        var merged = provider.Clone();

        merged.Title = document.Title ?? provider.Title;
        merged.OriginalTitle = document.OriginalTitle ?? provider.OriginalTitle;
        merged.SortTitle = document.SortTitle ?? provider.SortTitle;
        merged.Overview = document.Overview ?? provider.Overview;
        merged.Tagline = document.Tagline ?? provider.Tagline;
        merged.ContentRating = document.ContentRating ?? provider.ContentRating;
        merged.RuntimeMinutes = document.RuntimeMinutes ?? provider.RuntimeMinutes;
        merged.ReleaseDate = ReleaseDate(provider, document);
        merged.Status = provider.Status ?? document.Status;

        merged.TmdbId = provider.TmdbId ?? document.TmdbId;
        merged.ImdbId = provider.ImdbId ?? document.ImdbId;
        merged.TvdbId = provider.TvdbId ?? document.TvdbId;

        merged.HasMidCreditsStinger = provider.HasMidCreditsStinger || document.HasMidCreditsStinger;
        merged.HasPostCreditsStinger = provider.HasPostCreditsStinger || document.HasPostCreditsStinger;

        merged.Collection = document.Collection ?? provider.Collection;
        merged.GenreIds = document.GenreIds.Count > 0 ? document.GenreIds.ToList() : provider.GenreIds;
        merged.Cast = document.Cast.Count > 0 ? Cast(provider, document) : provider.Cast;
        merged.Videos = document.Videos
            .Concat(provider.Videos.Where(video => document.Videos.All(own => own.Key != video.Key)))
            .ToList();

        return merged;
    }

    private static DateOnly? ReleaseDate(MetadataResult provider, MetadataResult document)
    {
        if (document.ReleaseDate is not DateOnly declared) return provider.ReleaseDate;

        var onlyTheYearIsKnown = declared is { Month: 1, Day: 1 };
        return onlyTheYearIsKnown && provider.ReleaseDate?.Year == declared.Year
            ? provider.ReleaseDate
            : declared;
    }

    private static List<CastMemberResult> Cast(MetadataResult provider, MetadataResult document)
    {
        var byTmdbId = provider.Cast.Where(member => member.TmdbId != 0).GroupBy(member => member.TmdbId).ToDictionary(group => group.Key, group => group.First());
        var byTvdbId = provider.Cast.Where(member => member.TvdbId != 0).GroupBy(member => member.TvdbId).ToDictionary(group => group.Key, group => group.First());
        var byName = provider.Cast.GroupBy(member => member.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return document.Cast.Select(member =>
        {
            var known = member.TmdbId != 0 && byTmdbId.TryGetValue(member.TmdbId, out var byTmdb) ? byTmdb
                : member.TvdbId != 0 && byTvdbId.TryGetValue(member.TvdbId, out var byTvdb) ? byTvdb
                : byName.GetValueOrDefault(member.Name);

            return new CastMemberResult
            {
                Name = member.Name,
                CharacterName = member.CharacterName,
                Roles = member.Roles,
                TmdbId = member.TmdbId != 0 ? member.TmdbId : known?.TmdbId ?? 0,
                TvdbId = member.TvdbId != 0 ? member.TvdbId : known?.TvdbId ?? 0,
                ProfileImageUrl = member.ProfileImageUrl ?? known?.ProfileImageUrl
            };
        }).ToList();
    }
}
