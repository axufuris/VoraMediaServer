using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Discovery;
using Vora.Domain.Entities.Discovery;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Discovery;

public class DiscoveryManagerWatchlistTests
{
    private readonly IDiscoveryRepository _repo;
    private readonly DiscoveryManager _manager;

    public DiscoveryManagerWatchlistTests()
    {
        _repo = Substitute.For<IDiscoveryRepository>();
        _manager = new DiscoveryManager(
            Array.Empty<IDiscoveryProvider>(),
            Array.Empty<IDiscoveryTheaterProvider>(),
            _repo,
            NullLogger<DiscoveryManager>.Instance);
    }

    [Fact]
    public async Task ToggleWatchlistAsync_removes_when_already_present()
    {
        var profileId = Guid.NewGuid();
        _repo.IsInWatchlistAsync(profileId, "603", "tmdb").Returns(true);

        await _manager.ToggleWatchlistAsync(profileId, "603", "tmdb", "Movie", "The Matrix", null, null);

        await _repo.Received(1).RemoveFromWatchlistAsync(profileId, "603", "tmdb");
        await _repo.DidNotReceive().AddToWatchlistAsync(Arg.Any<UserWatchlistItem>());
    }

    [Fact]
    public async Task ToggleWatchlistAsync_adds_when_absent()
    {
        var profileId = Guid.NewGuid();
        _repo.IsInWatchlistAsync(profileId, "603", "tmdb").Returns(false);

        await _manager.ToggleWatchlistAsync(profileId, "603", "tmdb", "Movie", "The Matrix", "poster.jpg", new DateTime(2025, 1, 1));

        await _repo.Received(1).AddToWatchlistAsync(Arg.Is<UserWatchlistItem>(w =>
            w.ProfileId == profileId &&
            w.ExternalId == "603" &&
            w.ProviderId == "tmdb" &&
            w.Type == "Movie" &&
            w.Title == "The Matrix" &&
            w.PosterUrl == "poster.jpg" &&
            w.ExpectedReleaseDate == new DateTime(2025, 1, 1)));
        await _repo.DidNotReceive().RemoveFromWatchlistAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ToggleWatchlistAsync_propagates_repository_exceptions()
    {
        var profileId = Guid.NewGuid();
        _repo.IsInWatchlistAsync(profileId, "603", "tmdb").Returns(false);
        _repo.When(r => r.AddToWatchlistAsync(Arg.Any<UserWatchlistItem>()))
             .Do(_ => throw new InvalidOperationException("DB went sideways"));

        var act = () => _manager.ToggleWatchlistAsync(profileId, "603", "tmdb", "Movie", "x", null, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CheckWatchlistStatusAsync_delegates_to_repository()
    {
        var profileId = Guid.NewGuid();
        _repo.IsInWatchlistAsync(profileId, "603", "tmdb").Returns(true);

        var result = await _manager.CheckWatchlistStatusAsync(profileId, "603", "tmdb");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetWatchlistAsync_returns_repository_results()
    {
        var profileId = Guid.NewGuid();
        var entries = new List<UserWatchlistItem>
        {
            new() { ProfileId = profileId, ExternalId = "1", ProviderId = "tmdb", Type = "Movie", Title = "A" },
            new() { ProfileId = profileId, ExternalId = "2", ProviderId = "tmdb", Type = "TvShow", Title = "B" }
        };
        _repo.GetWatchlistAsync(profileId).Returns(entries);

        var result = await _manager.GetWatchlistAsync(profileId);

        result.Should().BeEquivalentTo(entries);
    }
}
