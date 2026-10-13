using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Ai;
using Vora.Application.Ai.Dtos;
using Vora.Application.Recommendations;
using Vora.Domain.Entities.Ai;

namespace Vora.Application.Tests.Ai;

// Movies and shows are embedded through the shared AI client, so the monthly
// token limit covers them, and every vector is saved with the model that made
// it: vectors from two models cannot be compared, so a change of model has to
// be visible on each row.
public class MediaEmbeddingServiceTests
{
    private const string Model = "text-embedding-3-small";

    private readonly IOpenAiRecommendationRepository _repository = Substitute.For<IOpenAiRecommendationRepository>();
    private readonly IOpenAiClient _openAi = Substitute.For<IOpenAiClient>();
    private readonly List<MediaItemEmbedding> _saved = new();

    public MediaEmbeddingServiceTests()
    {
        _openAi.GetEmbeddingModelAsync().Returns(Model);
        _repository.SaveEmbeddingsAsync(Arg.Do<List<MediaItemEmbedding>>(_saved.AddRange)).Returns(Task.CompletedTask);
    }

    private MediaEmbeddingService Service(List<MediaItemForEmbeddingDto> waiting, params float[]?[] vectors)
    {
        _repository.GetMediaItemsMissingEmbeddingsAsync(Arg.Any<int>(), Arg.Any<string>()).Returns(waiting);
        _openAi.EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(vectors.ToList());
        return new MediaEmbeddingService(_repository, _openAi, NullLogger<MediaEmbeddingService>.Instance);
    }

    private static List<MediaItemForEmbeddingDto> Items(params string[] titles) =>
        titles.Select(t => new MediaItemForEmbeddingDto
        {
            Id = Guid.NewGuid(),
            Title = t,
            Genres = new List<string> { "Science Fiction" },
            Cast = new List<string> { "Amy Adams" },
        }).ToList();

    private float[] VectorFor(Guid mediaItemId)
    {
        var embedding = _saved.Single(e => e.MediaItemId == mediaItemId).Embedding;
        if (embedding is null) throw new Xunit.Sdk.XunitException($"No embedding was saved for {mediaItemId}.");
        return embedding.ToArray();
    }

    [Fact]
    public async Task Each_vector_is_saved_against_its_title_with_the_model_that_made_it()
    {
        var items = Items("First", "Second", "Third");
        var service = Service(items, new[] { 1f }, new[] { 2f }, new[] { 3f });

        var processed = await service.ProcessMissingEmbeddingsAsync(3, TestContext.Current.CancellationToken);

        processed.Should().Be(3);
        VectorFor(items[0].Id).Should().Equal(1f);
        VectorFor(items[1].Id).Should().Equal(2f);
        VectorFor(items[2].Id).Should().Equal(3f);
        _saved.Should().OnlyContain(e => e.Model == Model);
    }

    [Fact]
    public async Task What_is_sent_describes_the_title()
    {
        var service = Service(Items("Arrival"), new[] { 1f });

        await service.ProcessMissingEmbeddingsAsync(1, TestContext.Current.CancellationToken);

        await _openAi.Received(1).EmbedAsync(
            AiConnection.PluginId,
            Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "Title: Arrival. Genres: Science Fiction. Cast: Amy Adams. Synopsis: Unknown"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_titles_the_current_model_has_not_read_are_asked_for()
    {
        _openAi.GetEmbeddingModelAsync().Returns("nomic-embed-text");
        var service = Service(Items("Arrival"), new[] { 1f });

        await service.ProcessMissingEmbeddingsAsync(50, TestContext.Current.CancellationToken);

        await _repository.Received(1).GetMediaItemsMissingEmbeddingsAsync(50, "nomic-embed-text");
        _saved.Should().ContainSingle().Which.Model.Should().Be("nomic-embed-text");
    }

    // Indexing past the end of a short response threw, so the whole batch went
    // unsaved and was paid for again on the next run.
    [Fact]
    public async Task A_short_response_saves_what_came_back_instead_of_throwing()
    {
        var service = Service(Items("First", "Second", "Third"), new[] { 1f }, new[] { 2f });

        var processed = await service.ProcessMissingEmbeddingsAsync(3, TestContext.Current.CancellationToken);

        processed.Should().Be(2);
        _saved.Should().HaveCount(2);
    }

    // The caller drains while a FULL batch comes back. Reporting the requested
    // count after a partial save would hand it the same unsaved items forever.
    [Fact]
    public async Task Reports_the_number_actually_saved_so_the_drain_loop_stops()
    {
        var items = Items("First", "Second", "Third");
        var service = Service(items, new[] { 1f }, null, null);

        var processed = await service.ProcessMissingEmbeddingsAsync(batchSize: 3, TestContext.Current.CancellationToken);

        processed.Should().Be(1);
        _saved.Should().ContainSingle().Which.MediaItemId.Should().Be(items[0].Id);
    }

    [Fact]
    public async Task Saves_nothing_and_reports_nothing_when_the_response_is_empty()
    {
        var service = Service(Items("First"));

        var processed = await service.ProcessMissingEmbeddingsAsync(1, TestContext.Current.CancellationToken);

        processed.Should().Be(0);
        await _repository.DidNotReceive().SaveEmbeddingsAsync(Arg.Any<List<MediaItemEmbedding>>());
    }

    [Fact]
    public async Task Does_not_call_the_api_when_nothing_is_missing()
    {
        var service = Service(new List<MediaItemForEmbeddingDto>());

        var processed = await service.ProcessMissingEmbeddingsAsync(100, TestContext.Current.CancellationToken);

        processed.Should().Be(0);
        await _openAi.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_nothing_when_there_is_no_embedding_model_to_use()
    {
        _openAi.GetEmbeddingModelAsync().Returns((string?)null);
        var service = Service(Items("First"), new[] { 1f });

        var processed = await service.ProcessMissingEmbeddingsAsync(100, TestContext.Current.CancellationToken);

        processed.Should().Be(0);
        await _repository.DidNotReceive().GetMediaItemsMissingEmbeddingsAsync(Arg.Any<int>(), Arg.Any<string>());
        await _openAi.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_monthly_limit_stops_the_run_and_saves_nothing()
    {
        var service = Service(Items("First"));
        _openAi.EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<float[]?>?>>(_ => throw new InvalidOperationException("AI monthly token limit reached"));

        var act = () => service.ProcessMissingEmbeddingsAsync(100, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _saved.Should().BeEmpty();
    }
}
