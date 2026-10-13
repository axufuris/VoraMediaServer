using Vora.Application.Metadata.Local;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Metadata;

public class LocalMetadataMergeTests
{
    private static MetadataResult Provider() => new()
    {
        Title = "Online title",
        Overview = "Online plot",
        ReleaseDate = new DateOnly(2003, 9, 19),
        Status = "Released",
        ContentRating = "R",
        TmdbId = "277",
        GenreIds = [28, 14],
        Cast = [new CastMemberResult { Name = "Kate Beckinsale", TmdbId = 3967 }],
        Seasons = [new SeasonResult { SeasonNumber = 1 }]
    };

    [Fact]
    public void Fields_the_nfo_leaves_empty_keep_the_providers_value()
    {
        var merged = LocalMetadataMerge.Overlay(Provider(), new MetadataResult { Title = "NFO title" });

        merged.Title.Should().Be("NFO title");
        merged.Overview.Should().Be("Online plot");
        merged.ContentRating.Should().Be("R");
        merged.GenreIds.Should().Equal(28, 14);
        merged.Cast.Should().ContainSingle(member => member.Name == "Kate Beckinsale");
        merged.Seasons.Should().ContainSingle();
    }

    [Fact]
    public void The_providers_copy_is_not_changed()
    {
        var provider = Provider();

        LocalMetadataMerge.Overlay(provider, new MetadataResult { Title = "NFO title", GenreIds = [53], Cast = [new CastMemberResult { Name = "Someone" }] });

        provider.Title.Should().Be("Online title");
        provider.GenreIds.Should().Equal(28, 14);
        provider.Cast.Should().ContainSingle(member => member.Name == "Kate Beckinsale");
    }

    [Fact]
    public void A_year_on_its_own_does_not_replace_the_providers_full_date_in_that_year()
    {
        var merged = LocalMetadataMerge.Overlay(Provider(), new MetadataResult { ReleaseDate = new DateOnly(2003, 1, 1) });

        merged.ReleaseDate.Should().Be(new DateOnly(2003, 9, 19));
    }

    [Fact]
    public void A_date_in_another_year_is_taken_from_the_nfo()
    {
        var merged = LocalMetadataMerge.Overlay(Provider(), new MetadataResult { ReleaseDate = new DateOnly(2004, 1, 1) });

        merged.ReleaseDate.Should().Be(new DateOnly(2004, 1, 1));
    }

    [Fact]
    public void The_providers_status_and_ids_win_because_other_features_read_them()
    {
        var merged = LocalMetadataMerge.Overlay(Provider(), new MetadataResult { Status = "Continuing", TmdbId = "9", ImdbId = "tt0320691" });

        merged.Status.Should().Be("Released");
        merged.TmdbId.Should().Be("277");
        merged.ImdbId.Should().Be("tt0320691");
    }

    [Fact]
    public void A_stinger_flagged_by_either_side_is_kept()
    {
        var merged = LocalMetadataMerge.Overlay(Provider(), new MetadataResult { HasPostCreditsStinger = true });

        merged.HasPostCreditsStinger.Should().BeTrue();
        merged.HasMidCreditsStinger.Should().BeFalse();
    }

    [Fact]
    public void Nfo_cast_members_are_matched_to_the_providers_people_by_id_before_name()
    {
        var provider = Provider();
        provider.Cast =
        [
            new CastMemberResult { Name = "Kate Beckinsale", TmdbId = 3967, ProfileImageUrl = "https://image.tmdb.org/t/p/w185/kate.jpg" },
            new CastMemberResult { Name = "Michael Sheen", TmdbId = 3968, ProfileImageUrl = "https://image.tmdb.org/t/p/w185/michael.jpg" }
        ];
        var document = new MetadataResult
        {
            Cast =
            [
                new CastMemberResult { Name = "KATE BECKINSALE", CharacterName = "Selene" },
                new CastMemberResult { Name = "M. Sheen", TmdbId = 3968, CharacterName = "Lucian" },
                new CastMemberResult { Name = "Only In The NFO", CharacterName = "Extra" }
            ]
        };

        var merged = LocalMetadataMerge.Overlay(provider, document);

        merged.Cast.Should().BeEquivalentTo(new[]
        {
            new CastMemberResult { Name = "KATE BECKINSALE", CharacterName = "Selene", TmdbId = 3967, ProfileImageUrl = "https://image.tmdb.org/t/p/w185/kate.jpg" },
            new CastMemberResult { Name = "M. Sheen", CharacterName = "Lucian", TmdbId = 3968, ProfileImageUrl = "https://image.tmdb.org/t/p/w185/michael.jpg" },
            new CastMemberResult { Name = "Only In The NFO", CharacterName = "Extra" }
        }, options => options.WithStrictOrdering());
    }
}
