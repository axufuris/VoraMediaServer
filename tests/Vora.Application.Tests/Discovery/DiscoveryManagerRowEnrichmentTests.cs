using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Discovery;
using Vora.Application.Media;
using Vora.Application.Requests;
using Vora.Domain.Entities.Discovery;
using Vora.Domain.Entities.Requests;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Discovery;

public class DiscoveryManagerRowEnrichmentTests
{
    private readonly IDiscoveryRepository _discoveryRepo = Substitute.For<IDiscoveryRepository>();
    private readonly IMediaRepository _mediaRepo = Substitute.For<IMediaRepository>();
    private readonly IRequestRepository _requestRepo = Substitute.For<IRequestRepository>();
    private readonly IDiscoveryProvider _provider = Substitute.For<IDiscoveryProvider>();

    private DiscoveryManager BuildManager()
    {
        _provider.Id.Returns("tmdb");
        _discoveryRepo.GetRowConfigsAsync().Returns(new List<DiscoveryRowConfig>
        {
            new() { Id = Guid.NewGuid(), ProviderId = "tmdb", RowId = "trending", Name = "Trending", OrderIndex = 0, IsEnabled = true }
        });
        return new DiscoveryManager(
            new[] { _provider },
            Array.Empty<IDiscoveryTheaterProvider>(),
            _discoveryRepo,
            _mediaRepo,
            _requestRepo,
            NullLogger<DiscoveryManager>.Instance);
    }

    [Fact]
    public async Task GetRowItemsAsync_flags_items_that_already_exist_in_library()
    {
        _provider.GetRowItemsAsync("trending", 1, Arg.Any<CancellationToken>()).Returns(new List<DiscoveryItemDto>
        {
            new() { ExternalId = "603", ProviderId = "tmdb", Title = "The Matrix", Type = "Movie" },
            new() { ExternalId = "604", ProviderId = "tmdb", Title = "The Matrix Reloaded", Type = "Movie" }
        });
        _mediaRepo.GetExistingExternalIdsAsync(Arg.Any<IEnumerable<string>>(), "Movie")
            .Returns(new HashSet<string> { "603" });
        _requestRepo.GetRequestsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>())
            .Returns(new Dictionary<string, MediaRequest>());

        var manager = BuildManager();
        var result = (await manager.GetRowItemsAsync("tmdb", "trending", cancellationToken: TestContext.Current.CancellationToken)).ToList();

        result.Should().HaveCount(2);
        result.Single(r => r.ExternalId == "603").InLibrary.Should().BeTrue();
        result.Single(r => r.ExternalId == "604").InLibrary.Should().BeFalse();
    }

    [Fact]
    public async Task GetRowItemsAsync_attaches_request_status_when_request_exists()
    {
        _provider.GetRowItemsAsync("trending", 1, Arg.Any<CancellationToken>()).Returns(new List<DiscoveryItemDto>
        {
            new() { ExternalId = "603", ProviderId = "tmdb", Title = "The Matrix", Type = "Movie" }
        });
        _mediaRepo.GetExistingExternalIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>())
            .Returns(new HashSet<string>());
        _requestRepo.GetRequestsAsync(Arg.Any<IEnumerable<string>>(), "Movie")
            .Returns(new Dictionary<string, MediaRequest>
            {
                ["603"] = new MediaRequest
                {
                    Id = Guid.NewGuid(),
                    ExternalId = "603",
                    ProviderId = "tmdb",
                    Type = "Movie",
                    Title = "The Matrix",
                    Status = RequestStatus.Approved
                }
            });

        var manager = BuildManager();
        var result = (await manager.GetRowItemsAsync("tmdb", "trending", cancellationToken: TestContext.Current.CancellationToken)).ToList();

        result.Should().ContainSingle().Which.RequestStatus.Should().Be(RequestStatus.Approved);
    }

    [Fact]
    public async Task GetRowItemsAsync_leaves_request_status_null_when_no_request_exists()
    {
        _provider.GetRowItemsAsync("trending", 1, Arg.Any<CancellationToken>()).Returns(new List<DiscoveryItemDto>
        {
            new() { ExternalId = "999", ProviderId = "tmdb", Title = "Unrequested", Type = "Movie" }
        });
        _mediaRepo.GetExistingExternalIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>())
            .Returns(new HashSet<string>());
        _requestRepo.GetRequestsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>())
            .Returns(new Dictionary<string, MediaRequest>());

        var manager = BuildManager();
        var result = (await manager.GetRowItemsAsync("tmdb", "trending", cancellationToken: TestContext.Current.CancellationToken)).ToList();

        result.Should().ContainSingle().Which.RequestStatus.Should().BeNull();
    }
}
