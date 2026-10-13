using Microsoft.Extensions.Logging;
using Vora.Application.Ai;
using Vora.Domain.Entities.Ai;

namespace Vora.Application.Recommendations;

public interface IMediaEmbeddingService
{
    Task<int> ProcessMissingEmbeddingsAsync(int batchSize = 100, CancellationToken cancellationToken = default);
}

public class MediaEmbeddingService : IMediaEmbeddingService
{
    private readonly IOpenAiRecommendationRepository _repository;
    private readonly IOpenAiClient _openAi;
    private readonly ILogger<MediaEmbeddingService> _logger;

    public MediaEmbeddingService(IOpenAiRecommendationRepository repository, IOpenAiClient openAi, ILogger<MediaEmbeddingService> logger)
    {
        _repository = repository;
        _openAi = openAi;
        _logger = logger;
    }

    public async Task<int> ProcessMissingEmbeddingsAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var model = await _openAi.GetEmbeddingModelAsync();
        if (string.IsNullOrEmpty(model)) return 0;

        var itemsToProcess = await _repository.GetMediaItemsMissingEmbeddingsAsync(batchSize, model);
        if (!itemsToProcess.Any()) return 0;

        var inputStrings = itemsToProcess.Select(item =>
            $"Title: {item.Title}. " +
            $"Genres: {string.Join(", ", item.Genres)}. " +
            $"Cast: {string.Join(", ", item.Cast)}. " +
            $"Synopsis: {item.Overview ?? "Unknown"}"
        ).ToList();

        var vectors = await _openAi.EmbedAsync(AiConnection.PluginId, inputStrings, cancellationToken);
        if (vectors == null) return 0;

        // A response that does not cover every input is survivable: take what
        // came back rather than failing, which left the whole batch unsaved to
        // be paid for again the next night.
        var newEmbeddings = new List<MediaItemEmbedding>();
        for (int i = 0; i < itemsToProcess.Count && i < vectors.Count; i++)
        {
            if (vectors[i] is not float[] vector) continue;

            newEmbeddings.Add(new MediaItemEmbedding
            {
                MediaItemId = itemsToProcess[i].Id,
                Embedding = new Pgvector.Vector(vector),
                Model = model
            });
        }

        if (newEmbeddings.Count < itemsToProcess.Count)
        {
            _logger.LogWarning(
                "Embedding response covered {Returned} of {Requested} item(s); saving what came back and leaving the rest for the next run.",
                newEmbeddings.Count, itemsToProcess.Count);
        }

        if (newEmbeddings.Count == 0) return 0;

        await _repository.SaveEmbeddingsAsync(newEmbeddings);

        // The caller drains while a full batch comes back, and re-queries for items
        // that still have no embedding. Returning the REQUESTED count after a
        // partial save would hand it the same unsaved items forever, paying for
        // them on every pass.
        return newEmbeddings.Count;
    }
}
