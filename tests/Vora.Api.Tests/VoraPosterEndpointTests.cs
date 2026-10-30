using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Application.Artwork;

namespace Vora.Api.Tests;

public sealed class VoraPosterEndpointTests(VoraApiTestFactory factory) : IClassFixture<VoraApiTestFactory>
{
    private VoraPoster Halloween() =>
        factory.Services.GetRequiredService<IVoraPosterCatalog>().Posters.Single(poster => poster.Id == "halloween");

    [Fact]
    public async Task A_vora_poster_is_served_without_signing_in_and_kept_by_the_browser()
    {
        var halloween = Halloween();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(halloween.Address, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        response.Headers.CacheControl?.ToString().Should().Contain("immutable");
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(await File.ReadAllBytesAsync(halloween.FilePath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("holidays/gone.jpg")]
    [InlineData("holidays/halloween.png")]
    [InlineData("Holidays/halloween.jpg")]
    [InlineData("holidays/..%2Fposters.json")]
    [InlineData("..%2Fposters.json/x.jpg")]
    public async Task Only_a_shipped_poster_can_be_fetched(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/artwork/vora/{path}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_poster_list_needs_a_signed_in_viewer()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/artwork/vora", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_poster_list_names_every_poster_with_its_address()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid()));

        var catalog = await client.GetFromJsonAsync<VoraPosterCatalogVM>("/api/artwork/vora", TestContext.Current.CancellationToken);

        catalog.Should().NotBeNull();
        catalog?.Categories.Select(category => category.Name).Should().Equal("Holidays", "Popular", "Genres", "Decades", "Ratings");
        catalog?.Posters.Should().HaveCount(75);
        catalog?.Posters.Single(poster => poster.Id == "halloween").Url.Should().Be(Halloween().Address);
    }
}
