using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Podcasts;
using Vora.Domain.Entities.Podcasts;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Podcasts;

public class PodcastManagerTests
{
    // The feed-fetching paths (Subscribe/Refresh/Show) use HttpClient via IHttpClientFactory,
    // so those branches aren't exercised here. Tests cover the permission gate, the
    // unsubscribe lifecycle and the listing/state methods that go through the repo only.

    private readonly IPodcastRepository _repo;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IClientNotifier _notifier;
    private readonly List<IPodcastDiscoveryProvider> _discovery;
    private readonly PodcastManager _manager;

    public PodcastManagerTests()
    {
        _repo = Substitute.For<IPodcastRepository>();
        _httpFactory = Substitute.For<IHttpClientFactory>();
        _notifier = Substitute.For<IClientNotifier>();
        _discovery = new List<IPodcastDiscoveryProvider>();
        _manager = new PodcastManager(_repo, _httpFactory, _notifier, _discovery,
            NullLogger<PodcastManager>.Instance);
    }

    private static PodcastShow MakeShow(Guid id, string feedUrl, bool isInCatalog = false) => new()
    {
        Id = id,
        FeedUrl = feedUrl,
        Title = "Title",
        IsInCatalog = isInCatalog
    };

    private static PodcastSubscription MakeSubscription(Guid id, Guid profileId, Guid showId) => new()
    {
        Id = id,
        ProfileId = profileId,
        PodcastShowId = showId,
        Show = new PodcastShow { Id = showId, FeedUrl = "https://example.com/feed.xml", Title = "Show" }
    };

    // ---------- SubscribeAsync permission gate ----------

    [Fact]
    public async Task SubscribeAsync_throws_when_feed_url_blank()
    {
        var act = async () => await _manager.SubscribeAsync(Guid.NewGuid(), "   ", canAddCustomFeeds: true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Feed URL*required*");
    }

    [Fact]
    public async Task SubscribeAsync_throws_permission_denied_when_custom_disallowed_and_not_in_catalog()
    {
        _repo.IsShowInCatalogAsync(Arg.Any<string>()).Returns(false);

        var act = async () => await _manager.SubscribeAsync(Guid.NewGuid(), "https://x.com/f.xml", canAddCustomFeeds: false);

        await act.Should().ThrowAsync<PodcastPermissionDeniedException>().WithMessage("*catalog*");
    }

    [Fact]
    public async Task SubscribeAsync_trims_feed_url_before_catalog_check()
    {
        _repo.IsShowInCatalogAsync("https://x.com/feed.xml").Returns(false);

        var act = async () => await _manager.SubscribeAsync(Guid.NewGuid(), "  https://x.com/feed.xml  ", canAddCustomFeeds: false);

        await act.Should().ThrowAsync<PodcastPermissionDeniedException>();
        await _repo.Received(1).IsShowInCatalogAsync("https://x.com/feed.xml");
    }

    [Fact]
    public async Task SubscribeAsync_with_existing_subscription_returns_existing_without_creating_duplicate()
    {
        var profileId = Guid.NewGuid();
        var showId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var show = MakeShow(showId, "https://x.com/f.xml");

        _repo.IsShowInCatalogAsync(Arg.Any<string>()).Returns(true);
        _repo.GetShowByFeedUrlAsync("https://x.com/f.xml").Returns(show);
        _repo.GetSubscriptionsForProfileAsync(profileId).Returns(new List<PodcastSubscription>
        {
            MakeSubscription(subId, profileId, showId)
        });
        _repo.GetEpisodeCountsAsync(Arg.Any<IEnumerable<Guid>>()).Returns(new Dictionary<Guid, int> { [showId] = 5 });

        // Force the "existing show" path to skip the live refresh: the catch swallows any fetch failure
        // because the repository.GetShowByFeedUrlAsync returned a non-null show.
        // Trigger the catch by ensuring HttpClient throws — the substitute factory returns null, so the
        // actual HttpClient access will NRE and be caught by the manager's try/catch on line 109.
        _httpFactory.CreateClient(Arg.Any<string>()).Returns((HttpClient?)null!);

        var vm = await _manager.SubscribeAsync(profileId, "https://x.com/f.xml", canAddCustomFeeds: true);

        vm.Id.Should().Be(subId);
        await _repo.DidNotReceive().AddSubscriptionAsync(Arg.Any<PodcastSubscription>());
    }

    // ---------- UnsubscribeAsync ----------

    [Fact]
    public async Task UnsubscribeAsync_no_op_when_subscription_missing()
    {
        _repo.GetSubscriptionByIdAsync(Arg.Any<Guid>()).Returns((PodcastSubscription?)null);

        await _manager.UnsubscribeAsync(Guid.NewGuid(), Guid.NewGuid());

        await _repo.DidNotReceive().RemoveSubscriptionAsync(Arg.Any<PodcastSubscription>());
    }

    [Fact]
    public async Task UnsubscribeAsync_no_op_when_subscription_belongs_to_different_profile()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var sub = MakeSubscription(Guid.NewGuid(), ownerId, Guid.NewGuid());
        _repo.GetSubscriptionByIdAsync(sub.Id).Returns(sub);

        await _manager.UnsubscribeAsync(otherId, sub.Id);

        await _repo.DidNotReceive().RemoveSubscriptionAsync(Arg.Any<PodcastSubscription>());
    }

    [Fact]
    public async Task UnsubscribeAsync_removes_subscription_but_keeps_show_when_others_still_subscribe()
    {
        var profileId = Guid.NewGuid();
        var showId = Guid.NewGuid();
        var sub = MakeSubscription(Guid.NewGuid(), profileId, showId);
        _repo.GetSubscriptionByIdAsync(sub.Id).Returns(sub);
        _repo.ProfileHasOtherSubscriptionsAsync(showId, profileId).Returns(true);

        await _manager.UnsubscribeAsync(profileId, sub.Id);

        await _repo.Received(1).RemoveSubscriptionAsync(sub);
        await _repo.DidNotReceive().DeleteShowAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task UnsubscribeAsync_keeps_show_when_in_catalog_even_if_no_other_subscribers()
    {
        var profileId = Guid.NewGuid();
        var showId = Guid.NewGuid();
        var sub = MakeSubscription(Guid.NewGuid(), profileId, showId);
        _repo.GetSubscriptionByIdAsync(sub.Id).Returns(sub);
        _repo.ProfileHasOtherSubscriptionsAsync(showId, profileId).Returns(false);
        _repo.GetShowByIdAsync(showId).Returns(MakeShow(showId, "u", isInCatalog: true));

        await _manager.UnsubscribeAsync(profileId, sub.Id);

        await _repo.DidNotReceive().DeleteShowAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task UnsubscribeAsync_deletes_show_when_not_in_catalog_and_no_other_subscribers()
    {
        var profileId = Guid.NewGuid();
        var showId = Guid.NewGuid();
        var sub = MakeSubscription(Guid.NewGuid(), profileId, showId);
        _repo.GetSubscriptionByIdAsync(sub.Id).Returns(sub);
        _repo.ProfileHasOtherSubscriptionsAsync(showId, profileId).Returns(false);
        _repo.GetShowByIdAsync(showId).Returns(MakeShow(showId, "u", isInCatalog: false));

        await _manager.UnsubscribeAsync(profileId, sub.Id);

        await _repo.Received(1).DeleteShowAsync(showId);
    }

    // ---------- RefreshSubscriptionAsync ----------

    [Fact]
    public async Task RefreshSubscriptionAsync_throws_when_subscription_missing()
    {
        _repo.GetSubscriptionByIdAsync(Arg.Any<Guid>()).Returns((PodcastSubscription?)null);

        var act = async () => await _manager.RefreshSubscriptionAsync(Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Subscription not found*");
    }

    [Fact]
    public async Task RefreshSubscriptionAsync_throws_when_subscription_owned_by_other_profile()
    {
        var owner = Guid.NewGuid();
        var sub = MakeSubscription(Guid.NewGuid(), owner, Guid.NewGuid());
        _repo.GetSubscriptionByIdAsync(sub.Id).Returns(sub);

        var act = async () => await _manager.RefreshSubscriptionAsync(Guid.NewGuid(), sub.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Subscription not found*");
    }

    // ---------- RefreshShowAsync ----------

    [Fact]
    public async Task RefreshShowAsync_throws_when_show_missing()
    {
        _repo.GetShowByIdAsync(Arg.Any<Guid>()).Returns((PodcastShow?)null);

        var act = async () => await _manager.RefreshShowAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Show not found*");
    }

    // ---------- GetSubscriptionsAsync ----------

    [Fact]
    public async Task GetSubscriptionsAsync_maps_episode_counts_for_each_show()
    {
        var profileId = Guid.NewGuid();
        var showA = Guid.NewGuid();
        var showB = Guid.NewGuid();
        _repo.GetSubscriptionsForProfileAsync(profileId).Returns(new List<PodcastSubscription>
        {
            MakeSubscription(Guid.NewGuid(), profileId, showA),
            MakeSubscription(Guid.NewGuid(), profileId, showB)
        });
        _repo.GetEpisodeCountsAsync(Arg.Any<IEnumerable<Guid>>()).Returns(new Dictionary<Guid, int>
        {
            [showA] = 25,
            [showB] = 7
        });

        var result = await _manager.GetSubscriptionsAsync(profileId);

        result.Should().HaveCount(2);
        result.Single(s => s.ShowId == showA).EpisodeCount.Should().Be(25);
        result.Single(s => s.ShowId == showB).EpisodeCount.Should().Be(7);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_returns_zero_count_for_show_with_no_episodes_entry()
    {
        var profileId = Guid.NewGuid();
        var showId = Guid.NewGuid();
        _repo.GetSubscriptionsForProfileAsync(profileId).Returns(new List<PodcastSubscription>
        {
            MakeSubscription(Guid.NewGuid(), profileId, showId)
        });
        _repo.GetEpisodeCountsAsync(Arg.Any<IEnumerable<Guid>>()).Returns(new Dictionary<Guid, int>());

        var result = await _manager.GetSubscriptionsAsync(profileId);

        result.Single().EpisodeCount.Should().Be(0);
    }
}
