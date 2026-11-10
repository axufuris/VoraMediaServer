using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class ViewingHistoryClearTests
{
    private readonly Guid _sam = Guid.NewGuid();
    private readonly Guid _andy = Guid.NewGuid();
    private readonly VoraDbContext _db = new(new DbContextOptionsBuilder<VoraDbContext>()
        .UseInMemoryDatabase("viewing-history-" + Guid.NewGuid().ToString("N"))
        .Options);

    public ViewingHistoryClearTests()
    {
        _db.UserProfiles.AddRange(
            new UserProfile { Id = _sam, Name = "Sam", UserId = Guid.NewGuid() },
            new UserProfile { Id = _andy, Name = "Andy", UserId = Guid.NewGuid() });

        _db.UserMediaStates.AddRange(
            State(_sam, isPlayed: true, position: 0),
            State(_sam, isPlayed: true, position: 0),
            State(_sam, isPlayed: false, position: 1620),
            State(_sam, isPlayed: false, position: 0),
            State(_andy, isPlayed: true, position: 0));
        _db.UserMediaRatings.AddRange(Rating(_sam, 8m), Rating(_sam, 6.5m), Rating(_andy, 9m));
        _db.PreservedUserMediaData.AddRange(
            new PreservedUserMediaData { ProfileId = _sam, ContentKey = "both", HasState = true, IsPlayed = true, LastPlayedAt = DateTime.UtcNow, Rating = 7m, RatedAt = DateTime.UtcNow },
            new PreservedUserMediaData { ProfileId = _sam, ContentKey = "watched", HasState = true, ResumePositionSeconds = 300, LastPlayedAt = DateTime.UtcNow },
            new PreservedUserMediaData { ProfileId = _sam, ContentKey = "rated", Rating = 5m, RatedAt = DateTime.UtcNow },
            new PreservedUserMediaData { ProfileId = _andy, ContentKey = "both", HasState = true, IsPlayed = true, Rating = 9m });
        _db.SaveChanges();
    }

    private static UserMediaState State(Guid profileId, bool isPlayed, double position) =>
        new() { ProfileId = profileId, MediaItemId = Guid.NewGuid(), IsPlayed = isPlayed, ResumePositionSeconds = position };

    private static UserMediaRating Rating(Guid profileId, decimal rating) =>
        new() { ProfileId = profileId, MediaItemId = Guid.NewGuid(), Rating = rating };

    private UserMediaStateRepository Repository() => new(_db);

    [Fact]
    public async Task Counts_what_a_profile_has_watched_has_part_watched_and_has_rated()
    {
        var history = await Repository().GetViewingHistoryAsync(_sam);

        history.Should().BeEquivalentTo(new { WatchedCount = 2, InProgressCount = 1, RatingCount = 2 });
    }

    [Fact]
    public async Task Clearing_both_removes_the_profiles_watch_states_ratings_and_archived_copies()
    {
        var cleared = await Repository().ClearViewingHistoryAsync(_sam, watchHistory: true, ratings: true);

        cleared.Should().BeEquivalentTo(new { WatchStatesRemoved = 4, RatingsRemoved = 2 });
        (await _db.UserMediaStates.CountAsync(s => s.ProfileId == _sam, TestContext.Current.CancellationToken)).Should().Be(0);
        (await _db.UserMediaRatings.CountAsync(r => r.ProfileId == _sam, TestContext.Current.CancellationToken)).Should().Be(0);
        (await _db.PreservedUserMediaData.CountAsync(p => p.ProfileId == _sam, TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Another_profile_keeps_everything()
    {
        await Repository().ClearViewingHistoryAsync(_sam, watchHistory: true, ratings: true);

        (await _db.UserMediaStates.CountAsync(s => s.ProfileId == _andy, TestContext.Current.CancellationToken)).Should().Be(1);
        (await _db.UserMediaRatings.CountAsync(r => r.ProfileId == _andy, TestContext.Current.CancellationToken)).Should().Be(1);
        (await _db.PreservedUserMediaData.SingleAsync(p => p.ProfileId == _andy, TestContext.Current.CancellationToken)).Should().BeEquivalentTo(new { HasState = true, IsPlayed = true, Rating = 9m });
    }

    [Fact]
    public async Task Clearing_the_watch_history_keeps_the_ratings()
    {
        var cleared = await Repository().ClearViewingHistoryAsync(_sam, watchHistory: true, ratings: false);

        cleared.Should().BeEquivalentTo(new { WatchStatesRemoved = 4, RatingsRemoved = 0 });
        (await _db.UserMediaStates.CountAsync(s => s.ProfileId == _sam, TestContext.Current.CancellationToken)).Should().Be(0);
        (await _db.UserMediaRatings.CountAsync(r => r.ProfileId == _sam, TestContext.Current.CancellationToken)).Should().Be(2);
        var archived = await _db.PreservedUserMediaData.Where(p => p.ProfileId == _sam).OrderBy(p => p.ContentKey).ToListAsync(TestContext.Current.CancellationToken);
        archived.Select(p => (p.ContentKey, p.HasState, p.IsPlayed, p.LastPlayedAt, p.Rating)).Should().Equal(
            ("both", false, false, (DateTime?)null, (decimal?)7m),
            ("rated", false, false, (DateTime?)null, (decimal?)5m));
    }

    [Fact]
    public async Task Clearing_the_ratings_keeps_the_watch_history()
    {
        var cleared = await Repository().ClearViewingHistoryAsync(_sam, watchHistory: false, ratings: true);

        cleared.Should().BeEquivalentTo(new { WatchStatesRemoved = 0, RatingsRemoved = 2 });
        (await _db.UserMediaStates.CountAsync(s => s.ProfileId == _sam, TestContext.Current.CancellationToken)).Should().Be(4);
        (await _db.UserMediaRatings.CountAsync(r => r.ProfileId == _sam, TestContext.Current.CancellationToken)).Should().Be(0);
        var archived = await _db.PreservedUserMediaData.Where(p => p.ProfileId == _sam).OrderBy(p => p.ContentKey).ToListAsync(TestContext.Current.CancellationToken);
        archived.Select(p => (p.ContentKey, p.HasState, p.Rating, p.RatedAt)).Should().Equal(
            ("both", true, (decimal?)null, (DateTime?)null),
            ("watched", true, (decimal?)null, (DateTime?)null));
    }

    [Fact]
    public async Task An_unknown_profile_is_not_found()
    {
        (await Repository().GetViewingHistoryAsync(Guid.NewGuid())).Should().BeNull();
        (await Repository().ClearViewingHistoryAsync(Guid.NewGuid(), watchHistory: true, ratings: true)).Should().BeNull();
    }
}
