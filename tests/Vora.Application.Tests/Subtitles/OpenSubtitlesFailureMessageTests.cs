using System.Net;
using Vora.Plugins.Providers.OpenSubtitles;

namespace Vora.Application.Tests.Subtitles;

// A search that the provider REFUSED used to return an empty list, so a rejected
// API key and a title with genuinely no subtitles looked identical in the
// player: "No subtitles found for this language." These pin that a refusal now
// says what went wrong, because the wrong-credential case is both the most
// likely and the least guessable.
public class OpenSubtitlesFailureMessageTests
{
    // An OpenSubtitles API key is a short consumer key. A JWT is what /login
    // returns, and the two are easy to confuse on a page that shows both.
    private const string Jwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl";
    private const string RealKey = "aBcDeF1234567890aBcDeF1234567890";

    [Fact]
    public void A_login_token_pasted_as_an_api_key_is_recognised()
    {
        OpenSubtitlesSubtitleProvider.LooksLikeJwt(Jwt).Should().BeTrue();
    }

    [Theory]
    [InlineData(RealKey)]
    [InlineData("")]
    [InlineData(null)]
    // Three segments matter as much as the prefix: a key that merely starts the
    // same way is not a token.
    [InlineData("eyJnotatoken")]
    public void A_real_key_is_not_mistaken_for_a_token(string? value)
    {
        OpenSubtitlesSubtitleProvider.LooksLikeJwt(value).Should().BeFalse();
    }

    [Fact]
    public void A_rejected_login_token_says_which_value_is_wrong()
    {
        var message = OpenSubtitlesSubtitleProvider.DescribeSearchFailure(HttpStatusCode.Unauthorized, Jwt);

        message.Should().Contain("login token");
        message.Should().Contain("Consumers");
    }

    [Fact]
    public void A_rejected_real_key_points_at_the_plugin_settings()
    {
        var message = OpenSubtitlesSubtitleProvider.DescribeSearchFailure(HttpStatusCode.Forbidden, RealKey);

        message.Should().Contain("rejected the API key");
        message.Should().NotContain("login token");
    }

    [Fact]
    public void Rate_limiting_says_to_wait_rather_than_to_check_the_key()
    {
        var message = OpenSubtitlesSubtitleProvider.DescribeSearchFailure(HttpStatusCode.TooManyRequests, RealKey);

        message.Should().Contain("rate-limiting");
        message.Should().NotContain("API key");
    }

    [Fact]
    public void An_unexpected_status_is_still_reported_with_its_code()
    {
        OpenSubtitlesSubtitleProvider.DescribeSearchFailure(HttpStatusCode.ServiceUnavailable, RealKey)
            .Should().Contain("503");
    }

    // Every branch has to produce something a viewer can act on; an empty string
    // would put us back where we started.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public void No_failure_is_left_unexplained(HttpStatusCode status)
    {
        OpenSubtitlesSubtitleProvider.DescribeSearchFailure(status, RealKey).Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("tt1368337", "1368337")]
    [InlineData("tt0083658", "83658")]
    [InlineData("1368337", "1368337")]
    [InlineData("  tt1368337  ", "1368337")]
    public void An_imdb_id_is_reduced_to_the_number_the_api_wants(string stored, string expected)
    {
        OpenSubtitlesSubtitleProvider.NormalizeImdbId(stored).Should().Be(expected);
    }
}
