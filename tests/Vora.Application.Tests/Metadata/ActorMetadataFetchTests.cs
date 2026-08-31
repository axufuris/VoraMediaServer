using Vora.Application.Metadata;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Metadata;

// Actor.TmdbId is a TMDB person id, so the lookup has to go to the TMDB
// provider specifically. It used to take _providers.FirstOrDefault(), and
// registration order comes from assembly reflection — so it could land on the
// local provider (whose actor fetch returns null unconditionally, enriching
// nobody and logging nothing) or on TVDB (which would resolve the id in its own
// people id space and answer about a different person).
public class ActorMetadataFetchTests
{
    private static IMetadataProvider Provider(string id, ActorMetadataResult? result)
    {
        var provider = Substitute.For<IMetadataProvider>();
        provider.Id.Returns(id);
        provider.FetchActorMetadataAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(result);
        return provider;
    }

    private static MetadataFetchService Build(params IMetadataProvider[] providers) =>
        new(providers, Array.Empty<IRatingsProvider>(), Array.Empty<IArtworkProvider>());

    [Fact]
    public async Task Uses_the_tmdb_provider_even_when_another_is_registered_first()
    {
        var local = Provider("local_metadata", null);
        var tmdb = Provider("tmdb_metadata", new ActorMetadataResult { Biography = "A biography." });

        var result = await Build(local, tmdb).GetActorMetadataAsync(321432);

        result.Should().NotBeNull();
        result!.Biography.Should().Be("A biography.");
        await local.DidNotReceive().FetchActorMetadataAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // TVDB's people endpoint uses its own id space, so handing it a TMDB person
    // id would return somebody else entirely.
    [Fact]
    public async Task Never_asks_a_provider_that_uses_a_different_id_space()
    {
        var tvdb = Provider("tvdb_metadata", new ActorMetadataResult { Biography = "Wrong person." });
        var tmdb = Provider("tmdb_metadata", new ActorMetadataResult { Biography = "Right person." });

        var result = await Build(tvdb, tmdb).GetActorMetadataAsync(321432);

        result!.Biography.Should().Be("Right person.");
        await tvdb.DidNotReceive().FetchActorMetadataAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_null_when_tmdb_is_not_installed_rather_than_asking_someone_else()
    {
        var local = Provider("local_metadata", new ActorMetadataResult { Biography = "Should not be used." });

        (await Build(local).GetActorMetadataAsync(321432)).Should().BeNull();
        await local.DidNotReceive().FetchActorMetadataAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Does_not_call_out_for_an_actor_with_no_tmdb_id(int tmdbId)
    {
        var tmdb = Provider("tmdb_metadata", new ActorMetadataResult());

        (await Build(tmdb).GetActorMetadataAsync(tmdbId)).Should().BeNull();
        await tmdb.DidNotReceive().FetchActorMetadataAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Passes_the_actors_tmdb_id_through()
    {
        var tmdb = Provider("tmdb_metadata", new ActorMetadataResult());

        await Build(tmdb).GetActorMetadataAsync(321432);

        await tmdb.Received(1).FetchActorMetadataAsync(321432, Arg.Any<CancellationToken>());
    }
}
