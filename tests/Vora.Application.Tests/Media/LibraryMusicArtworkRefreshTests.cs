using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

// Artists and Albums are not MediaItems, so library enrichment walks straight
// past them. The per-artist refresh only ever ran at the moment an artist was
// created without embedded art — so an artist whose folder art filled
// ArtworkUrl never asked a provider for a background, banner or logo, and no
// rescan or metadata refresh would ever go back for them.
public class LibraryMusicArtworkRefreshTests : IDisposable
{
    private readonly string _artworkDir;
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly IMusicArtworkProvider _provider = Substitute.For<IMusicArtworkProvider>();
    private readonly MusicManager _manager;
    private readonly Guid _libraryId = Guid.NewGuid();

    public LibraryMusicArtworkRefreshTests()
    {
        _artworkDir = Path.Combine(Path.GetTempPath(), "vora-lib-artwork-" + Guid.NewGuid().ToString("N"));

        _manager = new MusicManager(
            _repository,
            Substitute.For<IUserRepository>(),
            Substitute.For<IUserMediaStateRepository>(),
            new[] { _provider },
            Array.Empty<ILyricsProvider>(),
            Array.Empty<IListeningDataProvider>(),
            Substitute.For<IClientNotifier>(),
            Options.Create(new StoragePathsOptions { CustomArtwork = _artworkDir }),
            new NullTaskProgressReporter(),
            NullLogger<MusicManager>.Instance);

        _repository.GetArtistIdsForArtworkRefreshAsync(Arg.Any<Guid>(), Arg.Any<bool>()).Returns(new List<Guid>());
        _repository.GetAlbumIdsForArtworkRefreshAsync(Arg.Any<Guid>(), Arg.Any<bool>()).Returns(new List<Guid>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_artworkDir)) Directory.Delete(_artworkDir, recursive: true);
    }

    private Artist GivenArtist(params MusicArtworkResult[] results)
    {
        var artist = new Artist { Id = Guid.NewGuid(), Name = "311", ArtworkUrl = "existing.jpg" };
        _repository.GetArtistIdsForArtworkRefreshAsync(_libraryId, Arg.Any<bool>()).Returns(new List<Guid> { artist.Id });
        _repository.GetArtistForUpdateAsync(artist.Id).Returns(artist);
        _repository.GetArtistByIdAsync(artist.Id, Arg.Any<MusicAccessFilter>()).Returns(artist);
        _provider.SearchArtistArtworkAsync(artist.Name, Arg.Any<CancellationToken>()).Returns(results);
        return artist;
    }

    // The case that was broken: an artist that already has a photo from folder
    // art, and has never been asked for the rest.
    [Fact]
    public async Task Fills_the_missing_slots_of_an_artist_that_already_has_a_photo()
    {
        var artist = GivenArtist(
            new MusicArtworkResult { Url = "bg.jpg", ProviderName = "t", Kind = MusicArtworkKind.Background },
            new MusicArtworkResult { Url = "banner.jpg", ProviderName = "t", Kind = MusicArtworkKind.Banner },
            new MusicArtworkResult { Url = "logo.png", ProviderName = "t", Kind = MusicArtworkKind.Logo });

        await _manager.RefreshLibraryArtworkFromProvidersAsync(_libraryId, force: false, TestContext.Current.CancellationToken);

        artist.ArtworkUrl.Should().Be("existing.jpg");
        artist.BackgroundUrl.Should().Be("bg.jpg");
        artist.BannerUrl.Should().Be("banner.jpg");
        artist.ClearLogoUrl.Should().Be("logo.png");
    }

    [Fact]
    public async Task Asks_the_repository_for_only_the_incomplete_artists_when_not_forced()
    {
        await _manager.RefreshLibraryArtworkFromProvidersAsync(_libraryId, force: false, TestContext.Current.CancellationToken);

        await _repository.Received(1).GetArtistIdsForArtworkRefreshAsync(_libraryId, false);
        await _repository.Received(1).GetAlbumIdsForArtworkRefreshAsync(_libraryId, false);
    }

    [Fact]
    public async Task Passes_force_through_to_the_target_queries()
    {
        await _manager.RefreshLibraryArtworkFromProvidersAsync(_libraryId, force: true, TestContext.Current.CancellationToken);

        await _repository.Received(1).GetArtistIdsForArtworkRefreshAsync(_libraryId, true);
        await _repository.Received(1).GetAlbumIdsForArtworkRefreshAsync(_libraryId, true);
    }

    // One artist whose provider lookup throws must not cost the library the rest.
    [Fact]
    public async Task One_failing_artist_does_not_stop_the_others()
    {
        var failing = new Artist { Id = Guid.NewGuid(), Name = "Broken" };
        var ok = new Artist { Id = Guid.NewGuid(), Name = "Fine" };

        _repository.GetArtistIdsForArtworkRefreshAsync(_libraryId, Arg.Any<bool>())
            .Returns(new List<Guid> { failing.Id, ok.Id });
        _repository.GetArtistForUpdateAsync(failing.Id).Returns<Artist?>(_ => throw new InvalidOperationException("boom"));
        _repository.GetArtistForUpdateAsync(ok.Id).Returns(ok);
        _repository.GetArtistByIdAsync(ok.Id, Arg.Any<MusicAccessFilter>()).Returns(ok);
        _provider.SearchArtistArtworkAsync("Fine", Arg.Any<CancellationToken>())
            .Returns(new[] { new MusicArtworkResult { Url = "thumb.jpg", ProviderName = "t", Kind = MusicArtworkKind.Thumb } });

        await _manager.RefreshLibraryArtworkFromProvidersAsync(_libraryId, force: false, TestContext.Current.CancellationToken);

        ok.ArtworkUrl.Should().Be("thumb.jpg");
    }

    [Fact]
    public async Task Does_nothing_when_every_item_is_already_complete()
    {
        await _manager.RefreshLibraryArtworkFromProvidersAsync(_libraryId, force: false, TestContext.Current.CancellationToken);

        await _provider.DidNotReceive().SearchArtistArtworkAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stops_when_cancelled()
    {
        GivenArtist(new MusicArtworkResult { Url = "bg.jpg", ProviderName = "t", Kind = MusicArtworkKind.Background });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var refresh = async () => await _manager.RefreshLibraryArtworkFromProvidersAsync(_libraryId, force: false, cts.Token);

        await refresh.Should().ThrowAsync<OperationCanceledException>();
    }
}
