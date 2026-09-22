using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Application.Settings;
using Vora.Domain.Entities.Ai;

namespace Vora.Application.Recommendations;

public interface IMediaEmbeddingService
{
    Task<int> ProcessMissingEmbeddingsAsync(int batchSize = 100, CancellationToken cancellationToken = default);
}

public class MediaEmbeddingService : IMediaEmbeddingService
{
    private readonly IOpenAiRecommendationRepository _repository;
    private readonly ISystemSettingsRepository _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MediaEmbeddingService> _logger;

    public MediaEmbeddingService(IOpenAiRecommendationRepository repository, ISystemSettingsRepository settings, HttpClient httpClient, ILogger<MediaEmbeddingService> logger)
    {
        _repository = repository;
        _settings = settings;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<int> ProcessMissingEmbeddingsAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var apiKey = await _settings.GetPluginSettingAsync("openai_recommendations", "api_key");
        if (string.IsNullOrWhiteSpace(apiKey)) return 0;

        var itemsToProcess = await _repository.GetMediaItemsMissingEmbeddingsAsync(batchSize);
        if (!itemsToProcess.Any()) return 0;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var inputStrings = itemsToProcess.Select(item =>
            $"Title: {item.Title}. " +
            $"Genres: {string.Join(", ", item.Genres)}. " +
            $"Cast: {string.Join(", ", item.Cast)}. " +
            $"Synopsis: {item.Overview ?? "Unknown"}"
        ).ToList();

        var response = await _httpClient.PostAsJsonAsync("https://api.openai.com/v1/embeddings", new
        {
            model = "text-embedding-3-small",
            input = inputStrings
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"OpenAI API Error: {error}");
        }

        var embedData = await response.Content.ReadFromJsonAsync<OpenAiBulkEmbedResponse>(cancellationToken);
        if (embedData?.Data == null || !embedData.Data.Any()) return 0;

        // Match on the index OpenAI returns, not on position in the response. The
        // API documents an index per entry precisely because the order is not part
        // of the contract, and pairing by position would quietly attach every
        // embedding to the wrong title — recommendations would look plausible and
        // be nonsense, with nothing failing to show for it.
        //
        // A response that does not cover every input is also survivable: take what
        // came back rather than indexing past the end, which threw and left the
        // whole batch unsaved to be paid for again the next night.
        var byIndex = embedData.Data
            .Where(d => d.Index >= 0 && d.Index < itemsToProcess.Count && d.Embedding.Length > 0)
            .GroupBy(d => d.Index)
            .ToDictionary(g => g.Key, g => g.First().Embedding);

        var newEmbeddings = new List<MediaItemEmbedding>();
        for (int i = 0; i < itemsToProcess.Count; i++)
        {
            if (!byIndex.TryGetValue(i, out var vector)) continue;

            newEmbeddings.Add(new MediaItemEmbedding
            {
                MediaItemId = itemsToProcess[i].Id,
                Embedding = new Pgvector.Vector(vector)
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

        await _repository.LogAiUsageAsync(new AiUsageLog
        {
            PluginId = "openai_recommendations",
            ModelUsed = "text-embedding-3-small",
            PromptTokens = embedData.Usage.Prompt_Tokens,
            TotalTokens = embedData.Usage.Total_Tokens,
            ProfileId = null
        });

        // The caller drains while a full batch comes back, and re-queries for items
        // that still have no embedding. Returning the REQUESTED count after a
        // partial save would hand it the same unsaved items forever, paying for
        // them on every pass.
        return newEmbeddings.Count;
    }

    private class OpenAiBulkEmbedResponse { public List<EmbedData> Data { get; set; } = new(); public Usage Usage { get; set; } = new(); }
    private class EmbedData { public float[] Embedding { get; set; } = Array.Empty<float>(); public int Index { get; set; } }
    private class Usage { public int Prompt_Tokens { get; set; } public int Total_Tokens { get; set; } }
}