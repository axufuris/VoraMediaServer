using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vora.Api.Extensions;
using Vora.Api.Tests.Infra;
using Vora.Application.Recommendations;
using Vora.Application.Recommendations.ViewModels;
using Vora.Application.SmartLists;
using Vora.Application.SmartLists.ViewModels;
using Vora.Application.Sync;
using Vora.Application.Sync.ViewModels;

namespace Vora.Api.Tests;

public class QueryGuidListTests
{
    private const string TestMovies = "6f1c2a7e-0d4b-4c55-9a3e-1b2c3d4e5f60";
    private const string TestShows = "0a9b8c7d-6e5f-4a3b-8c2d-1e0f9a8b7c6d";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(",,")]
    [InlineData("Music,42,not-a-guid")]
    public void Nothing_usable_leaves_nothing_out(string? csv)
    {
        QueryGuidList.Parse(csv).Should().BeEmpty();
    }

    [Fact]
    public void Ids_are_read_in_order_and_anything_else_is_skipped()
    {
        QueryGuidList.Parse($"{TestMovies},not-a-guid,, {TestShows} ,{TestMovies.ToUpperInvariant()}")
            .Should().Equal(Guid.Parse(TestMovies), Guid.Parse(TestShows));
    }
}

public sealed class ExcludeLibraryIdsEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private const string TestMovies = "6f1c2a7e-0d4b-4c55-9a3e-1b2c3d4e5f60";
    private const string TestShows = "0a9b8c7d-6e5f-4a3b-8c2d-1e0f9a8b7c6d";

    private readonly VoraApiTestFactory _factory;
    private readonly ISmartListManager _smartLists = Substitute.For<ISmartListManager>();
    private readonly ISyncAndStateManager _sync = Substitute.For<ISyncAndStateManager>();
    private readonly IRecommendationManager _recommendations = Substitute.For<IRecommendationManager>();
    private readonly Guid _profileId = Guid.NewGuid();

    public ExcludeLibraryIdsEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
        _smartLists.GetSmartListEntriesAsync(Arg.Any<Guid>(), Arg.Any<SmartListViewer>()).Returns(new List<SmartListEntryVM>());
        _sync.GetContinueWatchingFeedAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<List<Guid>?>()).Returns(new List<ContinueWatchingVM>());
        _recommendations.GetRecommendationsAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<List<Guid>>(), Arg.Any<List<string>>(), Arg.Any<List<string>>(),
                Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<List<Guid>?>())
            .Returns(new List<RecommendationListVM>());
    }

    public static TheoryData<string, string> Queries => new()
    {
        { "", "" },
        { "?excludeLibraryIds=", "" },
        { $"?excludeLibraryIds={TestMovies},{TestShows}", $"{TestMovies},{TestShows}" },
        { $"?excludeLibraryIds={TestMovies}%2Cnot-a-guid%2C%2C{TestShows}", $"{TestMovies},{TestShows}" },
        { "?excludeLibraryIds=Music,42", "" },
    };

    private HttpClient Client()
    {
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISmartListManager>();
            services.AddSingleton(_smartLists);
            services.RemoveAll<ISyncAndStateManager>();
            services.AddSingleton(_sync);
            services.RemoveAll<IRecommendationManager>();
            services.AddSingleton(_recommendations);
        }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), _profileId, isAdmin: true));
        return client;
    }

    private static List<Guid> Ids(string csv) =>
        csv.Length == 0 ? [] : csv.Split(',').Select(Guid.Parse).ToList();

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task A_home_row_reads_the_libraries_to_leave_out_from_the_query(string query, string expected)
    {
        var listId = Guid.NewGuid();

        var response = await Client().GetAsync($"/api/smartlists/{listId}/entries{query}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _smartLists.Received(1).GetSmartListEntriesAsync(listId,
            Arg.Is<SmartListViewer>(viewer => viewer.ProfileId == _profileId && viewer.ExcludedLibraryIds.SequenceEqual(Ids(expected))));
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Continue_watching_reads_the_libraries_to_leave_out_from_the_query(string query, string expected)
    {
        var response = await Client().GetAsync($"/api/sync/profiles/{_profileId}/continue-watching{query}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _sync.Received(1).GetContinueWatchingFeedAsync(
            Arg.Is(_profileId), Arg.Is(10), Arg.Is<List<Guid>?>(ids => ids != null && ids.SequenceEqual(Ids(expected))));
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task For_you_reads_the_libraries_to_leave_out_from_the_query(string query, string expected)
    {
        var separator = query.Length == 0 ? "?" : "&";

        var response = await Client().GetAsync($"/api/recommendations/global{query}{separator}providerId=local_recommendations", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _recommendations.Received(1).GetRecommendationsAsync(
            Arg.Is(_profileId), Arg.Is<Guid?>(library => library == null), Arg.Any<bool>(), Arg.Any<List<Guid>>(), Arg.Any<List<string>>(), Arg.Any<List<string>>(),
            Arg.Any<bool>(), Arg.Is<string?>("local_recommendations"), Arg.Is<List<Guid>?>(ids => ids != null && ids.SequenceEqual(Ids(expected))));
    }
}
