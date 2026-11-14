using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Collections;
using Vora.Application.Net;
using Vora.Application.Settings;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Library;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Tmdb;

namespace Vora.Application.Tests.Collections;

public sealed class CollectionArtworkLookupTests : IDisposable
{
    private readonly string _artworkFolder = Path.Combine(Path.GetTempPath(), "vora-collection-artwork-" + Guid.NewGuid().ToString("N"));
    private readonly ICollectionRepository _repository = Substitute.For<ICollectionRepository>();
    private readonly IArtworkProvider _tmdbArtwork = Substitute.For<IArtworkProvider>();
    private readonly IMetadataProvider _tmdbMetadata = Substitute.For<IMetadataProvider>();

    public CollectionArtworkLookupTests()
    {
        _tmdbArtwork.Id.Returns("tmdb_artwork");
        _tmdbMetadata.Id.Returns("tmdb_metadata");
        _tmdbArtwork.GetArtworkAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ArtworkResult>
            {
                new() { Url = "https://image.tmdb.org/t/p/original/poster.jpg", Kind = ArtworkKind.Poster, VoteAverage = 5.4 },
                new() { Url = "https://image.tmdb.org/t/p/original/backdrop.jpg", Kind = ArtworkKind.Backdrop },
            });
    }

    public void Dispose()
    {
        if (Directory.Exists(_artworkFolder)) Directory.Delete(_artworkFolder, true);
    }

    private CollectionArtworkService Service() => new(
        _repository,
        Options.Create(new StoragePathsOptions { CustomArtwork = _artworkFolder }),
        [_tmdbArtwork],
        [_tmdbMetadata],
        Substitute.For<ISafeImageDownloader>(),
        NullLogger<CollectionArtworkService>.Instance);

    private Task<IEnumerable<ArtworkResult>> AskedFor(string? tmdbId, string title) =>
        _tmdbArtwork.Received(1).GetArtworkAsync(tmdbId, null, null, "Collection", null, title, Arg.Any<CancellationToken>());

    [Fact]
    public async Task A_collection_not_yet_created_finds_its_artwork_through_the_tmdb_collection_its_title_names()
    {
        _tmdbMetadata.FindCollectionIdAsync("Star Wars", Arg.Any<CancellationToken>()).Returns(10);

        var found = await Service().FindProviderArtworkAsync("tmdb_artwork", " Star Wars ", TestContext.Current.CancellationToken);

        await AskedFor("10", "Star Wars");
        found.Select(a => (a.Type, a.Url, a.IsUserUploaded)).Should().Equal(
            ("Poster", "https://image.tmdb.org/t/p/original/poster.jpg", false),
            ("Backdrop", "https://image.tmdb.org/t/p/original/backdrop.jpg", false));
        found[0].VoteAverage.Should().Be(5.4);
        found.Select(a => a.Id).Should().OnlyHaveUniqueItems();
        await _repository.DidNotReceiveWithAnyArgs().ReplaceProviderArtworkAsync(default, default!);
    }

    [Fact]
    public async Task A_title_no_tmdb_collection_has_still_reaches_providers_that_search_by_title()
    {
        _tmdbMetadata.FindCollectionIdAsync("Christmas Classics", Arg.Any<CancellationToken>()).Returns((int?)null);

        await Service().FindProviderArtworkAsync("tmdb_artwork", "Christmas Classics", TestContext.Current.CancellationToken);

        await AskedFor(null, "Christmas Classics");
    }

    [Fact]
    public async Task A_failing_title_search_does_not_stop_the_lookup()
    {
        _tmdbMetadata.FindCollectionIdAsync("Alien", Arg.Any<CancellationToken>()).Returns<int?>(_ => throw new HttpRequestException("TMDB is down"));

        var found = await Service().FindProviderArtworkAsync("tmdb_artwork", "Alien", TestContext.Current.CancellationToken);

        await AskedFor(null, "Alien");
        found.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("unknown_artwork", "Star Wars")]
    [InlineData("tmdb_artwork", "  ")]
    public async Task An_unknown_provider_or_a_blank_title_finds_nothing(string providerId, string title)
    {
        var found = await Service().FindProviderArtworkAsync(providerId, title, TestContext.Current.CancellationToken);

        found.Should().BeEmpty();
        await _tmdbArtwork.DidNotReceiveWithAnyArgs().GetArtworkAsync(default, default, default, default!, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Refreshing_a_tmdb_collection_uses_its_own_id_without_searching()
    {
        var collection = new Collection { Title = "Star Wars Collection", TmdbId = 10 };
        _repository.GetForUpdateAsync(collection.Id).Returns(collection);

        await Service().RefreshProviderArtworkAsync(collection.Id, "tmdb_artwork");

        await AskedFor("10", "Star Wars Collection");
        await _tmdbMetadata.DidNotReceiveWithAnyArgs().FindCollectionIdAsync(default!, TestContext.Current.CancellationToken);
        await _repository.Received(1).ReplaceProviderArtworkAsync(collection.Id, Arg.Is<IEnumerable<CollectionArtwork>>(art =>
            art.Count() == 2 && art.All(a => a.CollectionId == collection.Id && a.ProviderId == "tmdb_artwork" && !a.IsUserUploaded)));
    }

    [Fact]
    public async Task Refreshing_a_hand_made_collection_looks_it_up_by_its_title()
    {
        var collection = new Collection { Title = "Harry Potter" };
        _repository.GetForUpdateAsync(collection.Id).Returns(collection);
        _tmdbMetadata.FindCollectionIdAsync("Harry Potter", Arg.Any<CancellationToken>()).Returns(1241);

        await Service().RefreshProviderArtworkAsync(collection.Id, "tmdb_artwork");

        await AskedFor("1241", "Harry Potter");
        await _repository.Received(1).ReplaceProviderArtworkAsync(collection.Id, Arg.Any<IEnumerable<CollectionArtwork>>());
    }
}

public class TmdbCollectionSearchTests
{
    private sealed class Answers(string body) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri ?? throw new InvalidOperationException("A request without an address."));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static IServiceScopeFactory Settings(string? apiKey)
    {
        var settings = Substitute.For<IPluginSettingsProvider>();
        settings.GetSettingAsync("tmdb_metadata", "api_key").Returns(apiKey);
        settings.GetMetadataLanguageAsync().Returns("eng");
        return new ServiceCollection().AddSingleton(settings).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task TMDB_names_the_first_collection_its_search_finds()
    {
        var tmdb = new Answers("""{ "results": [ { "id": 10, "name": "Star Wars Collection" }, { "id": 1, "name": "Star Wars: Clone Wars" } ] }""");
        var provider = new TmdbMetadataProvider(new HttpClient(tmdb), Settings("key"));

        var id = await provider.FindCollectionIdAsync(" Star Wars & Friends ", TestContext.Current.CancellationToken);

        id.Should().Be(10);
        var asked = tmdb.Asked.Should().ContainSingle().Subject;
        asked.AbsolutePath.Should().EndWith("/search/collection");
        asked.Query.Should().Contain("query=Star%20Wars%20%26%20Friends");
    }

    [Theory]
    [InlineData("""{ "results": [] }""")]
    [InlineData("""{ "results": [ { "name": "No id" } ] }""")]
    [InlineData("""{ "status_message": "Invalid API key" }""")]
    public async Task No_collection_comes_back_when_TMDB_finds_none(string body)
    {
        var provider = new TmdbMetadataProvider(new HttpClient(new Answers(body)), Settings("key"));

        (await provider.FindCollectionIdAsync("Christmas Classics", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task TMDB_is_not_asked_without_an_api_key()
    {
        var tmdb = new Answers("""{ "results": [ { "id": 10 } ] }""");
        var provider = new TmdbMetadataProvider(new HttpClient(tmdb), Settings(null));

        (await provider.FindCollectionIdAsync("Star Wars", TestContext.Current.CancellationToken)).Should().BeNull();
        tmdb.Asked.Should().BeEmpty();
    }
}
