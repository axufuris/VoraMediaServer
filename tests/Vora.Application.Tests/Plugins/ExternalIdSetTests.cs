using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Plugins;

public class ExternalIdSetTests
{
    [Fact]
    public void Empty_singleton_has_no_ids()
    {
        ExternalIdSet.Empty.TmdbId.Should().BeNull();
        ExternalIdSet.Empty.ImdbId.Should().BeNull();
        ExternalIdSet.Empty.TvdbId.Should().BeNull();
        ExternalIdSet.Empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void From_creates_set_with_supplied_ids()
    {
        var set = ExternalIdSet.From(tmdbId: "603", imdbId: "tt0133093");

        set.TmdbId.Should().Be("603");
        set.ImdbId.Should().Be("tt0133093");
        set.TvdbId.Should().BeNull();
        set.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_true_when_all_ids_null_or_whitespace()
    {
        ExternalIdSet.From(null, null, null).IsEmpty.Should().BeTrue();
        ExternalIdSet.From("  ", "\t", "").IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void IsEmpty_false_when_any_id_populated()
    {
        ExternalIdSet.From(tmdbId: "1").IsEmpty.Should().BeFalse();
        ExternalIdSet.From(imdbId: "tt1").IsEmpty.Should().BeFalse();
        ExternalIdSet.From(tvdbId: "2").IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Records_with_same_ids_are_equal()
    {
        var a = ExternalIdSet.From("603", "tt0133093", null);
        var b = ExternalIdSet.From("603", "tt0133093", null);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Records_with_different_ids_are_not_equal()
    {
        var a = ExternalIdSet.From("603");
        var b = ExternalIdSet.From("604");

        a.Should().NotBe(b);
    }

    [Fact]
    public void With_clones_with_overridden_field()
    {
        var original = ExternalIdSet.From("603");
        var updated = original with { ImdbId = "tt0133093" };

        updated.TmdbId.Should().Be("603");
        updated.ImdbId.Should().Be("tt0133093");
        original.ImdbId.Should().BeNull();
    }
}
