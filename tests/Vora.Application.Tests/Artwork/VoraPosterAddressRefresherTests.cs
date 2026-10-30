using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Artwork;
using Vora.Application.Collections;

namespace Vora.Application.Tests.Artwork;

public class VoraPosterAddressRefresherTests : IDisposable
{
    private const string CurrentHalloween = "/api/artwork/vora/holidays/halloween.jpg?v=new1";
    private const string CurrentChristmas = "/api/artwork/vora/holidays/christmas.jpg?v=new2";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vora-poster-refresh-" + Guid.NewGuid().ToString("N"));
    private readonly ICollectionRepository _collections = Substitute.For<ICollectionRepository>();
    private readonly IArtworkThumbnailService _thumbnails = Substitute.For<IArtworkThumbnailService>();

    public VoraPosterAddressRefresherTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "posters.json"), """
            {
              "categories": [ { "id": "holidays", "name": "Holidays" } ],
              "posters": [
                { "id": "halloween", "category": "holidays", "title": "Halloween", "aliases": ["halloween"], "version": "new1" },
                { "id": "christmas", "category": "holidays", "title": "Christmas", "aliases": ["christmas"], "version": "new2" }
              ]
            }
            """);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private VoraPosterAddressRefresher Refresher() => new(
        new VoraPosterCatalog(_directory, NullLogger<VoraPosterCatalog>.Instance),
        _collections,
        _thumbnails,
        NullLogger<VoraPosterAddressRefresher>.Instance);

    [Fact]
    public async Task A_collection_on_an_older_version_of_its_poster_moves_to_the_current_one()
    {
        var outdated = Guid.NewGuid();
        var current = Guid.NewGuid();
        var withdrawn = Guid.NewGuid();
        _collections.GetPosterAddressesStartingWithAsync(VoraPosterAddress.Prefix).Returns(
        [
            new CollectionPosterAddress(outdated, "/api/artwork/vora/holidays/halloween.jpg?v=old"),
            new CollectionPosterAddress(current, CurrentChristmas),
            new CollectionPosterAddress(withdrawn, "/api/artwork/vora/holidays/gone.jpg?v=1"),
        ]);

        var moved = await Refresher().RefreshAsync();

        moved.Should().Be(1);
        await _collections.Received(1).SetPosterUrlsAsync(Arg.Is<IReadOnlyDictionary<Guid, string>>(urls =>
            urls.Count == 1 && urls[outdated] == CurrentHalloween));
        _thumbnails.Received(1).RemoveThumbnailsForSources(
            Arg.Is<IEnumerable<string?>>(sources => sources.SequenceEqual(new[] { "/api/artwork/vora/holidays/halloween.jpg?v=old" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_is_written_when_every_collection_is_already_current()
    {
        _collections.GetPosterAddressesStartingWithAsync(VoraPosterAddress.Prefix).Returns(
        [
            new CollectionPosterAddress(Guid.NewGuid(), CurrentHalloween),
        ]);

        var moved = await Refresher().RefreshAsync();

        moved.Should().Be(0);
        await _collections.DidNotReceive().SetPosterUrlsAsync(Arg.Any<IReadOnlyDictionary<Guid, string>>());
        _thumbnails.DidNotReceive().RemoveThumbnailsForSources(Arg.Any<IEnumerable<string?>>(), Arg.Any<CancellationToken>());
    }
}
