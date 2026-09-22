using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Recommendations;
using Vora.Application.Ai.Dtos;
using Vora.Application.Settings;
using Vora.Domain.Entities.Ai;

namespace Vora.Application.Tests.Ai;

// Embeddings are paired back to media items from the API response. Pairing by
// POSITION rather than by the index the API returns would attach every vector to
// the wrong title — recommendations would look plausible and be nonsense, with
// nothing failing to show for it.
public class MediaEmbeddingServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _status;
        public int Calls { get; private set; }

        public StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _json = json;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
        }
    }

    private readonly IOpenAiRecommendationRepository _repository = Substitute.For<IOpenAiRecommendationRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();

    private static string Entry(int index, float value) =>
        $"{{\"index\":{index},\"embedding\":[{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}]}}";

    private static string Response(params string[] entries) =>
        "{\"data\":[" + string.Join(",", entries) + "],\"usage\":{\"prompt_tokens\":10,\"total_tokens\":10}}";

    private MediaEmbeddingService Build(StubHandler handler, List<MediaItemForEmbeddingDto> items)
    {
        _settings.GetPluginSettingAsync("openai_recommendations", "api_key").Returns("key");
        _repository.GetMediaItemsMissingEmbeddingsAsync(Arg.Any<int>()).Returns(items);

        return new MediaEmbeddingService(
            _repository,
            _settings,
            new HttpClient(handler),
            NullLogger<MediaEmbeddingService>.Instance);
    }

    private static List<MediaItemForEmbeddingDto> Items(params string[] titles) =>
        titles.Select(t => new MediaItemForEmbeddingDto
        {
            Id = Guid.NewGuid(),
            Title = t,
            Genres = new List<string>(),
            Cast = new List<string>(),
        }).ToList();

    // The bug this guards: entries arriving in a different order than requested.
    [Fact]
    public async Task Pairs_each_embedding_to_the_item_its_index_names()
    {
        var items = Items("First", "Second", "Third");
        var handler = new StubHandler(Response(Entry(2, 3f), Entry(0, 1f), Entry(1, 2f)));
        var service = Build(handler, items);

        var saved = new List<MediaItemEmbedding>();
        _repository.SaveEmbeddingsAsync(Arg.Do<List<MediaItemEmbedding>>(saved.AddRange)).Returns(Task.CompletedTask);

        await service.ProcessMissingEmbeddingsAsync(3, TestContext.Current.CancellationToken);

        saved.Should().HaveCount(3);
        VectorFor(saved, items[0].Id).Should().Equal(1f);
        VectorFor(saved, items[1].Id).Should().Equal(2f);
        VectorFor(saved, items[2].Id).Should().Equal(3f);
    }

    private static float[] VectorFor(List<MediaItemEmbedding> saved, Guid mediaItemId)
    {
        var embedding = saved.Single(e => e.MediaItemId == mediaItemId).Embedding;
        if (embedding is null) throw new Xunit.Sdk.XunitException($"No embedding was saved for {mediaItemId}.");
        return embedding.ToArray();
    }

    // Indexing past the end of a short response threw, so the whole batch went
    // unsaved and was paid for again on the next run.
    [Fact]
    public async Task A_short_response_saves_what_came_back_instead_of_throwing()
    {
        var items = Items("First", "Second", "Third");
        var handler = new StubHandler(Response(Entry(0, 1f), Entry(1, 2f)));
        var service = Build(handler, items);

        var processed = await service.ProcessMissingEmbeddingsAsync(3, TestContext.Current.CancellationToken);

        processed.Should().Be(2);
        await _repository.Received(1).SaveEmbeddingsAsync(Arg.Is<List<MediaItemEmbedding>>(l => l.Count == 2));
    }

    // The caller drains while a FULL batch comes back. Reporting the requested
    // count after a partial save would hand it the same unsaved items forever.
    [Fact]
    public async Task Reports_the_number_actually_saved_so_the_drain_loop_stops()
    {
        var items = Items("First", "Second", "Third");
        var handler = new StubHandler(Response(Entry(0, 1f)));
        var service = Build(handler, items);

        var processed = await service.ProcessMissingEmbeddingsAsync(batchSize: 3, TestContext.Current.CancellationToken);

        processed.Should().Be(1);
        processed.Should().BeLessThan(3);
    }

    [Fact]
    public async Task Saves_nothing_and_reports_nothing_when_the_response_is_empty()
    {
        var items = Items("First");
        var handler = new StubHandler(Response());
        var service = Build(handler, items);

        var processed = await service.ProcessMissingEmbeddingsAsync(1, TestContext.Current.CancellationToken);

        processed.Should().Be(0);
        await _repository.DidNotReceive().SaveEmbeddingsAsync(Arg.Any<List<MediaItemEmbedding>>());
    }

    [Fact]
    public async Task Does_not_call_the_api_when_nothing_is_missing()
    {
        var handler = new StubHandler(Response());
        var service = Build(handler, new List<MediaItemForEmbeddingDto>());

        var processed = await service.ProcessMissingEmbeddingsAsync(100, TestContext.Current.CancellationToken);

        processed.Should().Be(0);
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Does_not_call_the_api_without_a_key()
    {
        var handler = new StubHandler(Response());
        _settings.GetPluginSettingAsync("openai_recommendations", "api_key").Returns((string?)null);
        _repository.GetMediaItemsMissingEmbeddingsAsync(Arg.Any<int>()).Returns(Items("First"));
        var service = new MediaEmbeddingService(_repository, _settings, new HttpClient(handler), NullLogger<MediaEmbeddingService>.Instance);

        var processed = await service.ProcessMissingEmbeddingsAsync(100, TestContext.Current.CancellationToken);

        processed.Should().Be(0);
        handler.Calls.Should().Be(0);
    }
}
