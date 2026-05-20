using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Application.Settings;
using Vora.Domain.Entities.Ai;

namespace Vora.Application.Recommendations;

public interface IMediaEmbeddingService
{
    Task<int> ProcessMissingEmbeddingsAsync(int batchSize = 100);
}

public class MediaEmbeddingService : IMediaEmbeddingService
{
    private readonly IOpenAiRecommendationRepository _repository;
    private readonly ISystemSettingsRepository _settings;
    private readonly HttpClient _httpClient;

    public MediaEmbeddingService(IOpenAiRecommendationRepository repository, ISystemSettingsRepository settings, HttpClient httpClient)
    {
        _repository = repository;
        _settings = settings;
        _httpClient = httpClient;
    }

    public async Task<int> ProcessMissingEmbeddingsAsync(int batchSize = 100)
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
        });

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"OpenAI API Error: {error}");
        }

        var embedData = await response.Content.ReadFromJsonAsync<OpenAiBulkEmbedResponse>();
        if (embedData?.Data == null || !embedData.Data.Any()) return 0;

        var newEmbeddings = new List<MediaItemEmbedding>();
        for (int i = 0; i < itemsToProcess.Count; i++)
        {
            newEmbeddings.Add(new MediaItemEmbedding
            {
                MediaItemId = itemsToProcess[i].Id,
                Embedding = new Pgvector.Vector(embedData.Data[i].Embedding)
            });
        }

        await _repository.SaveEmbeddingsAsync(newEmbeddings);

        await _repository.LogAiUsageAsync(new AiUsageLog
        {
            PluginId = "openai_recommendations",
            ModelUsed = "text-embedding-3-small",
            PromptTokens = embedData.Usage.Prompt_Tokens,
            TotalTokens = embedData.Usage.Total_Tokens,
            ProfileId = null
        });

        return itemsToProcess.Count;
    }

    private class OpenAiBulkEmbedResponse { public List<EmbedData> Data { get; set; } = new(); public Usage Usage { get; set; } = new(); }
    private class EmbedData { public float[] Embedding { get; set; } = Array.Empty<float>(); public int Index { get; set; } }
    private class Usage { public int Prompt_Tokens { get; set; } public int Total_Tokens { get; set; } }
}