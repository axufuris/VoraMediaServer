using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

public class MixDetailTests
{
    private readonly IMusicRepository _music = Substitute.For<IMusicRepository>();
    private readonly IMusicRecommendationRepository _recommendations = Substitute.For<IMusicRecommendationRepository>();
    private readonly Guid _profileId = Guid.NewGuid();

    [Fact]
    public async Task Each_song_in_a_mix_carries_its_own_album_cover()
    {
        var high = new Track { Id = Guid.NewGuid(), Title = "High", Album = new Album { Title = "So Far So Good", ArtworkUrl = "/so-far-so-good.jpg" } };
        var rapGod = new Track { Id = Guid.NewGuid(), Title = "Rap God", Album = new Album { Title = "The Marshall Mathers LP2", ArtworkUrl = "/mmlp2.jpg" } };
        var noCover = new Track { Id = Guid.NewGuid(), Title = "Spaces", Album = new Album { Title = "Spaces", Artist = new Artist { Name = "BUNT.", ArtworkUrl = "/bunt.jpg" } } };
        var mix = new GeneratedMix { Id = Guid.NewGuid(), ProfileId = _profileId, Name = "Energizing workout beats", Kind = GeneratedMixKind.Requested, ArtworkUrl = "/mmlp2.jpg", TrackOrder = new List<Guid> { high.Id, rapGod.Id, noCover.Id } };
        _recommendations.GetMixByIdAsync(mix.Id, _profileId).Returns(mix);
        _recommendations.GetTracksByIdsAsync(mix.TrackOrder, Arg.Any<MusicAccessFilter>()).Returns(new List<Track> { high, rapGod, noCover });
        _music.GetLikedTrackIdsAsync(_profileId, Arg.Any<IEnumerable<Guid>>()).Returns(new HashSet<Guid>());
        var manager = new MusicRecommendationManager(
            _recommendations,
            _music,
            Substitute.For<IUserRepository>(),
            Substitute.For<ISystemSettingsRepository>(),
            Substitute.For<IClientNotifier>(),
            Array.Empty<IListeningDataProvider>(),
            NullLogger<MusicRecommendationManager>.Instance);

        var detail = await manager.GetMixDetailAsync(mix.Id, _profileId, MusicAccessFilter.Unrestricted);

        detail.Should().NotBeNull();
        detail?.Tracks.Select(t => t.AlbumArtworkUrl).Should().Equal("/so-far-so-good.jpg", "/mmlp2.jpg", "/bunt.jpg");
    }
}
