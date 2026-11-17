using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Requests;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class RequestQueueCountTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("request-queue-count-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static MediaRequest Request(string externalId, string type, RequestStatus status) =>
        new() { ExternalId = externalId, Type = type, Title = "Title " + externalId, ProviderId = "tmdb_discovery", Status = status };

    [Fact]
    public async Task Counts_the_requests_waiting_for_a_decision_of_both_kinds_and_no_others()
    {
        await using var db = NewContext();
        db.MediaRequests.AddRange(
            Request("1", "Movie", RequestStatus.Pending),
            Request("2", "Movie", RequestStatus.Pending),
            Request("3", "TvShow", RequestStatus.Pending),
            Request("4", "Movie", RequestStatus.Approved),
            Request("5", "Movie", RequestStatus.Processing),
            Request("6", "TvShow", RequestStatus.Available),
            Request("7", "TvShow", RequestStatus.Denied));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await new RequestRepository(db).GetPendingCountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Is_zero_when_nothing_is_waiting()
    {
        await using var db = NewContext();
        db.MediaRequests.Add(Request("1", "Movie", RequestStatus.Processing));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await new RequestRepository(db).GetPendingCountAsync()).Should().Be(0);
    }
}
