using Vora.Application.Posters;

namespace Vora.Application.Tests.Posters;

public class OriginalArtworkCacheFileNameTests
{
    [Fact]
    public void A_tmdb_poster_is_cached_under_its_original_size_path()
    {
        PosterOverlayManager.OriginalArtworkCacheFileName("https://image.tmdb.org/t/p/w500/abc123.jpg")
            .Should().Be("tporiginalabc123.jpg");
    }

    [Theory]
    [InlineData("/api/artwork/custom/media_1_poster.jpg")]
    [InlineData("")]
    [InlineData("http://")]
    public void Anything_that_is_not_a_web_image_has_no_cache_name(string url)
    {
        PosterOverlayManager.OriginalArtworkCacheFileName(url).Should().BeNull();
    }
}
