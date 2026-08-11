using Vora.Application.Media.Dtos;

namespace Vora.Application.Collections;

public static class CollectionMembershipResolver
{
    public static List<Guid> Resolve(IEnumerable<CollectionMembershipEntry> entries, CollectionMatchCandidatesDto candidates)
    {
        var movieLookup = BuildTitleLookup(candidates.Movies);
        var showLookup = BuildTitleLookup(candidates.Shows);
        var seasonLookup = candidates.Seasons
            .GroupBy(s => (s.TvShowId, s.SeasonNumber))
            .ToDictionary(g => g.Key, g => g.First().Id);

        var matched = new List<Guid>();

        foreach (var entry in entries)
        {
            if (string.Equals(entry.MediaType, "Season", StringComparison.OrdinalIgnoreCase))
            {
                if (entry.SeasonNumber == null) continue;

                var seasonMatched = false;
                foreach (var showKey in TitleMatch.MatchKeys(entry.ShowTitle))
                {
                    if (!showLookup.TryGetValue(showKey, out var showMatches)) continue;

                    foreach (var (_, showId) in showMatches)
                    {
                        if (seasonLookup.TryGetValue((showId, entry.SeasonNumber.Value), out var seasonId))
                        {
                            matched.Add(seasonId);
                            seasonMatched = true;
                            break;
                        }
                    }
                    if (seasonMatched) break;
                }
                continue;
            }

            var lookup = string.Equals(entry.MediaType, "TvShow", StringComparison.OrdinalIgnoreCase)
                ? showLookup
                : movieLookup;

            var id = ResolveTitle(lookup, entry.Title, entry.Year);
            if (id != null)
            {
                matched.Add(id.Value);
            }
        }

        return matched.Distinct().ToList();
    }

    private static Dictionary<string, List<(int? Year, Guid Id)>> BuildTitleLookup(IEnumerable<MediaTitleCandidateDto> candidates)
    {
        var lookup = new Dictionary<string, List<(int? Year, Guid Id)>>();
        foreach (var candidate in candidates)
        {
            foreach (var key in TitleMatch.MatchKeys(candidate.Title))
            {
                if (!lookup.TryGetValue(key, out var list))
                {
                    list = new List<(int?, Guid)>();
                    lookup[key] = list;
                }
                list.Add((candidate.Year, candidate.Id));
            }
        }
        return lookup;
    }

    private static Guid? ResolveTitle(Dictionary<string, List<(int? Year, Guid Id)>> lookup, string? title, int? year)
    {
        var matches = new List<(int? Year, Guid Id)>();
        foreach (var key in TitleMatch.MatchKeys(title))
        {
            if (lookup.TryGetValue(key, out var list))
            {
                matches.AddRange(list);
            }
        }

        matches = matches.Distinct().ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        if (year != null)
        {
            var exact = matches.FirstOrDefault(m => m.Year == year);
            return exact.Id != Guid.Empty ? exact.Id : (Guid?)null;
        }

        return matches.Count == 1 ? matches[0].Id : (Guid?)null;
    }
}
