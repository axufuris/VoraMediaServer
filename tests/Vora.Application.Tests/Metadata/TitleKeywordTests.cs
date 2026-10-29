using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Metadata;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Tmdb;

namespace Vora.Application.Tests.Metadata;

public class TitleKeywordTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void A_movie_s_keywords_are_read_from_keywords_once_each_in_lowercase()
    {
        var keywords = Json("""
            { "id": 10439, "keywords": [ { "id": 3335, "name": "halloween" }, { "id": 1, "name": "Witch" }, { "id": 2, "name": "witch" }, { "id": 3, "name": " " } ] }
            """);

        TmdbMetadataProvider.KeywordNames(keywords, isShow: false).Should().Equal("halloween", "witch");
    }

    [Fact]
    public void A_show_s_keywords_are_read_from_results()
    {
        var keywords = Json("""
            { "id": 1399, "results": [ { "id": 207317, "name": "christmas" }, { "id": 9, "name": "Holiday" } ] }
            """);

        TmdbMetadataProvider.KeywordNames(keywords, isShow: true).Should().Equal("christmas", "holiday");
    }

    [Fact]
    public void A_title_without_keywords_has_none()
    {
        TmdbMetadataProvider.KeywordNames(Json("""{ "id": 7 }"""), isShow: false).Should().BeEmpty();
        TmdbMetadataProvider.KeywordNames(Json("""{ "id": 7, "results": [] }"""), isShow: true).Should().BeEmpty();
    }

    private sealed class FakeKeywordRepository : ITitleKeywordRepository
    {
        public readonly List<TitleAwaitingKeywords> Awaiting = new();
        public readonly Dictionary<Guid, List<string>> Saved = new();
        public int Pages;

        public Task<bool> HasTitlesAwaitingKeywordsAsync(Guid libraryId) => Task.FromResult(Awaiting.Count > 0);

        public Task<List<TitleAwaitingKeywords>> GetTitlesAwaitingKeywordsAsync(Guid libraryId, IReadOnlyCollection<Guid> skipped, int limit)
        {
            Pages++;
            return Task.FromResult(Awaiting.Where(t => !skipped.Contains(t.Id) && !Saved.ContainsKey(t.Id)).Take(limit).ToList());
        }

        public Task SaveKeywordsAsync(IReadOnlyDictionary<Guid, List<string>> keywords)
        {
            foreach (var (id, names) in keywords) Saved[id] = names;
            return Task.CompletedTask;
        }
    }

    private static TitleKeywordService Service(FakeKeywordRepository repository, IMetadataProvider provider) =>
        new(repository, new[] { provider }, NullLogger<TitleKeywordService>.Instance);

    [Fact]
    public async Task Every_title_waiting_for_keywords_gets_them_and_one_tmdb_has_none_for_is_not_asked_again()
    {
        var repository = new FakeKeywordRepository();
        var hocusPocus = new TitleAwaitingKeywords(Guid.NewGuid(), "10439", false);
        var community = new TitleAwaitingKeywords(Guid.NewGuid(), "18347", true);
        repository.Awaiting.AddRange(new[] { hocusPocus, community });
        var tmdb = Substitute.For<IMetadataProvider>();
        tmdb.FetchKeywordsAsync("10439", false, Arg.Any<CancellationToken>()).Returns(new List<string> { "halloween", "witch" });
        tmdb.FetchKeywordsAsync("18347", true, Arg.Any<CancellationToken>()).Returns(new List<string>());

        var filled = await Service(repository, tmdb).FillAwaitingAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        filled.Should().Be(2);
        repository.Saved[hocusPocus.Id].Should().Equal("halloween", "witch");
        repository.Saved[community.Id].Should().BeEmpty();
    }

    [Fact]
    public async Task A_title_whose_fetch_failed_is_left_for_the_next_scan_and_the_rest_go_on()
    {
        var repository = new FakeKeywordRepository();
        var failing = Enumerable.Range(0, TitleKeywordService.PageSize).Select(i => new TitleAwaitingKeywords(Guid.NewGuid(), $"f{i}", false)).ToList();
        var answered = new TitleAwaitingKeywords(Guid.NewGuid(), "ok", false);
        repository.Awaiting.AddRange(failing.Take(50));
        repository.Awaiting.Add(answered);
        var tmdb = Substitute.For<IMetadataProvider>();
        tmdb.FetchKeywordsAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns((List<string>?)null);
        tmdb.FetchKeywordsAsync("ok", false, Arg.Any<CancellationToken>()).Returns(new List<string> { "christmas" });

        var filled = await Service(repository, tmdb).FillAwaitingAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        filled.Should().Be(1);
        repository.Saved.Keys.Should().Equal(answered.Id);
        repository.Pages.Should().Be(2);
    }

    [Fact]
    public async Task When_tmdb_answers_nothing_at_all_the_pass_stops_after_one_page()
    {
        var repository = new FakeKeywordRepository();
        repository.Awaiting.AddRange(Enumerable.Range(0, TitleKeywordService.PageSize * 3).Select(i => new TitleAwaitingKeywords(Guid.NewGuid(), $"t{i}", false)));
        var tmdb = Substitute.For<IMetadataProvider>();
        tmdb.FetchKeywordsAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns((List<string>?)null);

        var filled = await Service(repository, tmdb).FillAwaitingAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        filled.Should().Be(0);
        repository.Pages.Should().Be(1);
    }
}
