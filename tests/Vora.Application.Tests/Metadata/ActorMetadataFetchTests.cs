using Vora.Application.Metadata;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Metadata;

// An actor is identified by whichever provider supplied them, so the lookup has
// to follow that id space. It used to take _providers.FirstOrDefault(), and
// registration order comes from assembly reflection — so it could land on the
// local provider (whose actor fetch returns null unconditionally, enriching
// nobody and logging nothing) or hand a TMDB id to TVDB, which would resolve it
// in TVDB's own people id space and answer about a different person.
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

        var result = await Build(local, tmdb).GetActorMetadataAsync(321432, 0);

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

        var result = await Build(tvdb, tmdb).GetActorMetadataAsync(321432, 0);

        result!.Biography.Should().Be("Right person.");
        await tvdb.DidNotReceive().FetchActorMetadataAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_null_when_tmdb_is_not_installed_rather_than_asking_someone_else()
    {
        var local = Provider("local_metadata", new ActorMetadataResult { Biography = "Should not be used." });

        (await Build(local).GetActorMetadataAsync(321432, 0)).Should().BeNull();
        await local.DidNotReceive().FetchActorMetadataAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // A TVDB-sourced cast member has no TMDB id, so the lookup has to follow the
    // other id space rather than giving up — a server running only TVDB would
    // otherwise never enrich anyone.
    [Fact]
    public async Task Uses_the_tvdb_provider_when_the_actor_only_has_a_tvdb_id()
    {
        var tmdb = Provider("tmdb_metadata", new ActorMetadataResult { Biography = "Wrong id space." });
        var tvdb = Provider("tvdb_metadata", new ActorMetadataResult { Biography = "From TVDB." });

        var result = await Build(tmdb, tvdb).GetActorMetadataAsync(0, 55123);

        result!.Biography.Should().Be("From TVDB.");
        await tvdb.Received(1).FetchActorMetadataAsync(55123, Arg.Any<CancellationToken>());
        await tmdb.DidNotReceive().FetchActorMetadataAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prefers_tmdb_when_the_actor_carries_both_ids()
    {
        var tmdb = Provider("tmdb_metadata", new ActorMetadataResult { Biography = "From TMDB." });
        var tvdb = Provider("tvdb_metadata", new ActorMetadataResult { Biography = "From TVDB." });

        var result = await Build(tmdb, tvdb).GetActorMetadataAsync(321432, 55123);

        result!.Biography.Should().Be("From TMDB.");
        await tvdb.DidNotReceive().FetchActorMetadataAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Does_not_call_out_for_an_actor_with_no_id_at_all(int tmdbId)
    {
        var tmdb = Provider("tmdb_metadata", new ActorMetadataResult());

        (await Build(tmdb).GetActorMetadataAsync(tmdbId, 0)).Should().BeNull();
        await tmdb.DidNotReceive().FetchActorMetadataAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Passes_the_actors_tmdb_id_through()
    {
        var tmdb = Provider("tmdb_metadata", new ActorMetadataResult());

        await Build(tmdb).GetActorMetadataAsync(321432, 0);

        await tmdb.Received(1).FetchActorMetadataAsync(321432, Arg.Any<CancellationToken>());
    }
}
