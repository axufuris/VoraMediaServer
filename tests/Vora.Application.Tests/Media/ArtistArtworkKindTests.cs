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

// Providers return several kinds of image from one lookup. Before results
// carried a Kind they arrived as one undifferentiated list, the first was taken
// as the artist photo and the rest were discarded — so background, banner and
// clear logo were fetched on every refresh and never stored.
public class ArtistArtworkKindTests : IDisposable
{
    private readonly string _artworkDir;
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly IMusicArtworkProvider _provider = Substitute.For<IMusicArtworkProvider>();
    private readonly MusicManager _manager;

    public ArtistArtworkKindTests()
    {
        _artworkDir = Path.Combine(Path.GetTempPath(), "vora-artwork-kind-" + Guid.NewGuid().ToString("N"));

        _manager = new MusicManager(
            _repository,
            Substitute.For<IUserRepository>(),
            Substitute.For<IUserMediaStateRepository>(),
            new[] { _provider },
            Array.Empty<ILyricsProvider>(),
            Array.Empty<IListeningDataProvider>(),
            Substitute.For<IClientNotifier>(),
            Options.Create(new StoragePathsOptions { CustomArtwork = _artworkDir }),
            NullLogger<MusicManager>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_artworkDir)) Directory.Delete(_artworkDir, recursive: true);
    }

    private static MusicArtworkResult Art(string url, MusicArtworkKind kind) =>
        new() { Url = url, ProviderName = "test", Kind = kind };

    private Artist GivenArtist(Artist artist, params MusicArtworkResult[] results)
    {
        _repository.GetArtistForUpdateAsync(artist.Id).Returns(artist);
        _repository.GetArtistByIdAsync(artist.Id, Arg.Any<MusicAccessFilter>()).Returns(artist);
        _provider.SearchArtistArtworkAsync(artist.Name, Arg.Any<CancellationToken>())
            .Returns(results);
        return artist;
    }

    private static Artist NewArtist() => new() { Id = Guid.NewGuid(), Name = "Beastie Boys" };

    [Fact]
    public async Task Fills_every_slot_from_its_matching_kind()
    {
        var artist = GivenArtist(NewArtist(),
            Art("thumb.jpg", MusicArtworkKind.Thumb),
            Art("bg.jpg", MusicArtworkKind.Background),
            Art("banner.jpg", MusicArtworkKind.Banner),
            Art("logo.png", MusicArtworkKind.Logo));

        await _manager.RefreshArtistArtworkFromProvidersAsync(artist.Id, force: false, CancellationToken.None);

        artist.ArtworkUrl.Should().Be("thumb.jpg");
        artist.BackgroundUrl.Should().Be("bg.jpg");
        artist.BannerUrl.Should().Be("banner.jpg");
        artist.ClearLogoUrl.Should().Be("logo.png");
    }

    // A wordmark logo is not a photo of the band. Assigning by position put
    // whatever came back first into the artist image.
    [Fact]
    public async Task Does_not_use_a_logo_as_the_artist_image()
    {
        var artist = GivenArtist(NewArtist(),
            Art("logo.png", MusicArtworkKind.Logo),
            Art("thumb.jpg", MusicArtworkKind.Thumb));

        await _manager.RefreshArtistArtworkFromProvidersAsync(artist.Id, force: false, CancellationToken.None);

        artist.ArtworkUrl.Should().Be("thumb.jpg");
    }

    // A locked artist image used to return early, so it also blocked the three
    // slots that had nothing to do with it.
    [Fact]
    public async Task A_locked_artist_image_still_lets_the_other_slots_fill()
    {
        var artist = NewArtist();
        artist.ArtworkUrl = "kept.jpg";
        artist.LockedFields.Add(nameof(Artist.ArtworkUrl));
        GivenArtist(artist,
            Art("thumb.jpg", MusicArtworkKind.Thumb),
            Art("bg.jpg", MusicArtworkKind.Background));

        await _manager.RefreshArtistArtworkFromProvidersAsync(artist.Id, force: true, CancellationToken.None);

        artist.ArtworkUrl.Should().Be("kept.jpg");
        artist.BackgroundUrl.Should().Be("bg.jpg");
    }

    [Fact]
    public async Task Leaves_a_slot_alone_when_it_is_already_filled_and_not_forced()
    {
        var artist = NewArtist();
        artist.BackgroundUrl = "existing-bg.jpg";
        GivenArtist(artist,
            Art("thumb.jpg", MusicArtworkKind.Thumb),
            Art("new-bg.jpg", MusicArtworkKind.Background));

        await _manager.RefreshArtistArtworkFromProvidersAsync(artist.Id, force: false, CancellationToken.None);

        artist.BackgroundUrl.Should().Be("existing-bg.jpg");
        artist.ArtworkUrl.Should().Be("thumb.jpg");
    }

    // A provider that does not classify its results keeps working for the primary
    // image, but an unclassified photo must never be dropped into a background or
    // banner slot, where it would be the wrong shape.
    [Fact]
    public async Task Unclassified_results_fill_only_the_primary_image()
    {
        var artist = GivenArtist(NewArtist(), Art("mystery.jpg", MusicArtworkKind.Unknown));

        await _manager.RefreshArtistArtworkFromProvidersAsync(artist.Id, force: false, CancellationToken.None);

        artist.ArtworkUrl.Should().Be("mystery.jpg");
        artist.BackgroundUrl.Should().BeNull();
        artist.BannerUrl.Should().BeNull();
        artist.ClearLogoUrl.Should().BeNull();
    }

    [Fact]
    public async Task Saves_once_when_anything_was_applied()
    {
        var artist = GivenArtist(NewArtist(), Art("thumb.jpg", MusicArtworkKind.Thumb));

        await _manager.RefreshArtistArtworkFromProvidersAsync(artist.Id, force: false, CancellationToken.None);

        await _repository.Received(1).UpdateArtistAsync(artist);
    }

    [Fact]
    public async Task Does_not_save_when_nothing_matched()
    {
        var artist = GivenArtist(NewArtist(), Art("bg.jpg", MusicArtworkKind.Background));
        artist.BackgroundUrl = "already.jpg";

        await _manager.RefreshArtistArtworkFromProvidersAsync(artist.Id, force: false, CancellationToken.None);

        await _repository.DidNotReceive().UpdateArtistAsync(Arg.Any<Artist>());
    }
}
