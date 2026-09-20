using Vora.Application.Media;
using Xunit;

namespace Vora.Application.Tests.Media;

public class ContentIdentityTests
{
    [Fact]
    public void Movie_PrefersTmdbOverImdbAndTvdb()
    {
        var key = ContentIdentity.Compute("movie", "603", "tt0133093", "12345", null, null, null, null, null);
        Assert.Equal("movie:tmdb:603", key);
    }

    [Fact]
    public void Movie_FallsBackToImdbWhenNoTmdb()
    {
        var key = ContentIdentity.Compute("movie", null, "tt0133093", null, null, null, null, null, null);
        Assert.Equal("movie:imdb:tt0133093", key);
    }

    [Fact]
    public void Movie_WithNoExternalIds_IsNull()
    {
        var key = ContentIdentity.Compute("movie", null, null, null, null, null, null, null, null);
        Assert.Null(key);
    }

    [Fact]
    public void Show_UsesShowPrefix()
    {
        var key = ContentIdentity.Compute("show", "1399", null, null, null, null, null, null, null);
        Assert.Equal("show:tmdb:1399", key);
    }

    [Fact]
    public void Episode_CombinesSeriesIdWithSeasonAndEpisode()
    {
        var key = ContentIdentity.Compute("episode", null, null, null, 2, 5, "1399", null, null);
        Assert.Equal("episode:tmdb:1399:2:5", key);
    }

    [Fact]
    public void Episode_WithoutSeriesId_IsNull()
    {
        var key = ContentIdentity.Compute("episode", null, null, null, 2, 5, null, null, null);
        Assert.Null(key);
    }

    [Fact]
    public void Season_CombinesSeriesIdWithSeasonNumber()
    {
        var key = ContentIdentity.Compute("season", null, null, null, 3, null, null, null, "78901");
        Assert.Equal("season:tvdb:78901:3", key);
    }

    [Fact]
    public void SameEpisodeContent_ProducesStableKey_AcrossReImport()
    {
        var first = ContentIdentity.Compute("episode", null, null, null, 1, 1, "1399", "tt0944947", null);
        var second = ContentIdentity.Compute("episode", null, null, null, 1, 1, "1399", "tt0944947", null);
        Assert.Equal(first, second);
    }
}
