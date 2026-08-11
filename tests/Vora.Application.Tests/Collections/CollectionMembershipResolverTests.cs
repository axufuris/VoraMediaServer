using Vora.Application.Collections;
using Vora.Application.Media.Dtos;

namespace Vora.Application.Tests.Collections;

public class CollectionMembershipResolverTests
{
    private static CollectionMatchCandidatesDto Candidates(
        List<MediaTitleCandidateDto>? movies = null,
        List<MediaTitleCandidateDto>? shows = null,
        List<SeasonMatchCandidateDto>? seasons = null) => new()
        {
            Movies = movies ?? new(),
            Shows = shows ?? new(),
            Seasons = seasons ?? new()
        };

    [Fact]
    public void Matches_season_when_library_show_has_a_studio_possessive_prefix()
    {
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var candidates = Candidates(
            shows: new() { new MediaTitleCandidateDto { Id = showId, Title = "Marvel's Agents of S.H.I.E.L.D." } },
            seasons: new() { new SeasonMatchCandidateDto { Id = seasonId, TvShowId = showId, SeasonNumber = 3 } });
        var entries = new List<CollectionMembershipEntry>
        {
            new() { MediaType = "Season", ShowTitle = "Agents of S.H.I.E.L.D.", SeasonNumber = 3 }
        };

        CollectionMembershipResolver.Resolve(entries, candidates).Should().Equal(seasonId);
    }

    [Fact]
    public void Matches_movie_when_library_title_has_a_studio_possessive_prefix()
    {
        var id = Guid.NewGuid();
        var candidates = Candidates(movies: new()
        {
            new MediaTitleCandidateDto { Id = id, Title = "Marvel's The Avengers", Year = 2012 }
        });
        var entries = new List<CollectionMembershipEntry>
        {
            new() { MediaType = "Movie", Title = "The Avengers", Year = 2012 }
        };

        CollectionMembershipResolver.Resolve(entries, candidates).Should().Equal(id);
    }

    [Fact]
    public void Matches_movie_by_normalized_title_and_year()
    {
        var id = Guid.NewGuid();
        var candidates = Candidates(movies: new()
        {
            new MediaTitleCandidateDto { Id = id, Title = "Spider-Man: Homecoming", Year = 2017 }
        });
        var entries = new List<CollectionMembershipEntry>
        {
            new() { MediaType = "Movie", Title = "spider man homecoming", Year = 2017 }
        };

        CollectionMembershipResolver.Resolve(entries, candidates).Should().Equal(id);
    }

    [Fact]
    public void Leaves_ambiguous_movie_title_unmatched_when_no_year_disambiguates()
    {
        var candidates = Candidates(movies: new()
        {
            new MediaTitleCandidateDto { Id = Guid.NewGuid(), Title = "The Batman", Year = 2004 },
            new MediaTitleCandidateDto { Id = Guid.NewGuid(), Title = "The Batman", Year = 2022 }
        });
        var entries = new List<CollectionMembershipEntry>
        {
            new() { MediaType = "Movie", Title = "The Batman" }
        };

        CollectionMembershipResolver.Resolve(entries, candidates).Should().BeEmpty();
    }

    [Fact]
    public void Matches_short_film_stored_under_a_one_shot_designation_prefix()
    {
        var id = Guid.NewGuid();
        var candidates = Candidates(movies: new()
        {
            new MediaTitleCandidateDto { Id = id, Title = "Marvel One-Shot: Item 47", Year = 2012 }
        });
        var entries = new List<CollectionMembershipEntry>
        {
            new() { MediaType = "Movie", Title = "Item 47", Year = 2012 }
        };

        CollectionMembershipResolver.Resolve(entries, candidates).Should().Equal(id);
    }

    [Fact]
    public void Still_matches_a_short_film_by_its_full_designation_title()
    {
        var id = Guid.NewGuid();
        var candidates = Candidates(movies: new()
        {
            new MediaTitleCandidateDto { Id = id, Title = "Marvel One-Shot: Item 47", Year = 2012 }
        });
        var entries = new List<CollectionMembershipEntry>
        {
            new() { MediaType = "Movie", Title = "Marvel One-Shot: Item 47", Year = 2012 }
        };

        CollectionMembershipResolver.Resolve(entries, candidates).Should().Equal(id);
    }

    [Fact]
    public void Does_not_strip_a_regular_subtitle_prefix()
    {
        var candidates = Candidates(movies: new()
        {
            new MediaTitleCandidateDto { Id = Guid.NewGuid(), Title = "Spider-Man: Homecoming", Year = 2017 }
        });
        var entries = new List<CollectionMembershipEntry>
        {
            new() { MediaType = "Movie", Title = "Homecoming", Year = 2017 }
        };

        CollectionMembershipResolver.Resolve(entries, candidates).Should().BeEmpty();
    }

    [Fact]
    public void Does_not_match_a_same_titled_movie_of_a_different_year()
    {
        var candidates = Candidates(movies: new()
        {
            new MediaTitleCandidateDto { Id = Guid.NewGuid(), Title = "Daredevil", Year = 2003 }
        });
        var entries = new List<CollectionMembershipEntry>
        {
            new() { MediaType = "Movie", Title = "Daredevil", Year = 2015 }
        };

        CollectionMembershipResolver.Resolve(entries, candidates).Should().BeEmpty();
    }

    [Fact]
    public void Matches_a_season_whose_show_title_carries_a_trailing_year()
    {
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var candidates = Candidates(
            shows: new() { new MediaTitleCandidateDto { Id = showId, Title = "Hawkeye (2021)" } },
            seasons: new() { new SeasonMatchCandidateDto { Id = seasonId, TvShowId = showId, SeasonNumber = 1 } });
        var entries = new List<CollectionMembershipEntry>
        {
            new() { MediaType = "Season", ShowTitle = "Hawkeye", SeasonNumber = 1 }
        };

        CollectionMembershipResolver.Resolve(entries, candidates).Should().Equal(seasonId);
    }

    [Fact]
    public void Matches_season_by_show_title_and_number()
    {
        var showId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var candidates = Candidates(
            shows: new() { new MediaTitleCandidateDto { Id = showId, Title = "Loki" } },
            seasons: new() { new SeasonMatchCandidateDto { Id = seasonId, TvShowId = showId, SeasonNumber = 2 } });
        var entries = new List<CollectionMembershipEntry>
        {
            new() { MediaType = "Season", ShowTitle = "Loki", SeasonNumber = 2 }
        };

        CollectionMembershipResolver.Resolve(entries, candidates).Should().Equal(seasonId);
    }

    [Fact]
    public void Matches_season_even_when_show_title_has_duplicate_rows()
    {
        var showA = Guid.NewGuid();
        var showB = Guid.NewGuid();
        var seasonOnB = Guid.NewGuid();
        var candidates = Candidates(
            shows: new()
            {
                new MediaTitleCandidateDto { Id = showA, Title = "Hawkeye" },
                new MediaTitleCandidateDto { Id = showB, Title = "Hawkeye" }
            },
            seasons: new()
            {
                new SeasonMatchCandidateDto { Id = seasonOnB, TvShowId = showB, SeasonNumber = 1 }
            });
        var entries = new List<CollectionMembershipEntry>
        {
            new() { MediaType = "Season", ShowTitle = "Hawkeye", SeasonNumber = 1 }
        };

        CollectionMembershipResolver.Resolve(entries, candidates).Should().Equal(seasonOnB);
    }
}
