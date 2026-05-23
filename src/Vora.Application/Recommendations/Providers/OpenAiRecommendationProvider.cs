using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Vora.Application.Settings;
using Vora.Domain.Entities.Ai;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Recommendations.Providers;

public class OpenAiRecommendationProvider : IRecommendationProvider
{
    private readonly IOpenAiRecommendationRepository _repository;
    private readonly ISystemSettingsRepository _settings;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    public string Id => "openai_recommendations";
    public string Name => "OpenAI Smart Recommendations";
    public string Description => "Uses OpenAI to generate highly creative, personalized recommendation categories based on viewing history.";
    public string Version => "1.0.0";
    public string Type => "Recommendation";
    public bool IsSystemPlugin => true;
    public bool IsAiPlugin => true;

    public OpenAiRecommendationProvider(IOpenAiRecommendationRepository repository, ISystemSettingsRepository settings, HttpClient httpClient, IMemoryCache cache)
    {
        _repository = repository;
        _settings = settings;
        _httpClient = httpClient;
        _cache = cache;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto { Key = "api_key", Label = "OpenAI API Key", Type = "password", Description = "OpenAI secret key (sk-...). Create one at https://platform.openai.com/api-keys. Requires an OpenAI account with billing enabled (https://platform.openai.com/account/billing) — usage is metered. Used only for the nightly category-generation pass; cost is typically a few cents per refresh with the default gpt-4o-mini model." },
            new PluginSettingDefinitionDto { Key = "chat_model", Label = "Chat Model", Type = "text", DefaultValue = "gpt-4o-mini", Description = "Model used for generating categories." },
            new PluginSettingDefinitionDto { Key = "schedule_time", Label = "Nightly Vector Generation Time", Type = "time", DefaultValue = "02:00", Description = "Time to run the nightly AI vector generation (HH:mm format)." }
        };
    }

    public async Task<IEnumerable<RecommendationListDto>> GetRecommendationsAsync(Guid profileId, Guid? libraryId)
    {
        var cacheKey = $"ai_recs_{profileId}_{libraryId}";
        if (_cache.TryGetValue(cacheKey, out List<RecommendationListDto>? cachedLists) && cachedLists != null)
        {
            return cachedLists;
        }

        var isEnabledForUser = await _repository.IsAiEnabledForProfileAsync(profileId);
        if (!isEnabledForUser) return new List<RecommendationListDto>();

        var apiKey = await _settings.GetPluginSettingAsync(Id, "api_key");
        if (string.IsNullOrWhiteSpace(apiKey)) return new List<RecommendationListDto>();

        var chatModel = await _settings.GetPluginSettingAsync(Id, "chat_model") ?? "gpt-4o-mini";
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var lists = new List<RecommendationListDto>();
        var recentWatches = await _repository.GetRecentWatchHistoryContextAsync(profileId, 5);
        if (!recentWatches.Any()) return lists;

        var systemPrompt = "You are a movie/tv curator. The user recently watched: " + string.Join(", ", recentWatches) +
                           ". Invent 3 highly specific, creative recommendation category titles (e.g. 'Gritty Heists', 'Cozy Sci-Fi') they would love. " +
                           "Output valid JSON ONLY: { \"categories\": [ { \"title\": \"...\", \"description\": \"...\", \"vibe_keywords\": \"...\" } ] }";

        var chatResponse = await _httpClient.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", new
        {
            model = chatModel,
            messages = new[] { new { role = "user", content = systemPrompt } },
            response_format = new { type = "json_object" }
        });

        var chatData = await chatResponse.Content.ReadFromJsonAsync<OpenAiChatResponse>();
        if (chatData?.Choices == null || !chatData.Choices.Any()) return lists;

        await _repository.LogAiUsageAsync(new AiUsageLog
        {
            ProfileId = profileId,
            PluginId = Id,
            ModelUsed = chatModel,
            PromptTokens = chatData.Usage.Prompt_Tokens,
            CompletionTokens = chatData.Usage.Completion_Tokens,
            TotalTokens = chatData.Usage.Total_Tokens
        });

        var resultJson = chatData.Choices[0].Message.Content;
        var categories = JsonSerializer.Deserialize<OpenAiCategoryResult>(resultJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        int weight = 90;

        foreach (var cat in categories?.Categories ?? new List<CategoryDto>())
        {
            var embedResponse = await _httpClient.PostAsJsonAsync("https://api.openai.com/v1/embeddings", new
            {
                model = "text-embedding-3-small",
                input = $"{cat.Title} {cat.Description} {cat.Vibe_Keywords}"
            });

            var embedData = await embedResponse.Content.ReadFromJsonAsync<OpenAiEmbedResponse>();
            if (embedData?.Data == null || !embedData.Data.Any()) continue;

            await _repository.LogAiUsageAsync(new AiUsageLog
            {
                ProfileId = profileId,
                PluginId = Id,
                ModelUsed = "text-embedding-3-small",
                PromptTokens = embedData.Usage.Prompt_Tokens,
                TotalTokens = embedData.Usage.Total_Tokens
            });

            var searchVector = embedData.Data[0].Embedding;

            var localMediaIds = await _repository.VectorSearchUnwatchedMediaAsync(profileId, libraryId, searchVector, 15);

            if (localMediaIds.Any())
            {
                lists.Add(new RecommendationListDto
                {
                    Title = $"AI: {cat.Title}",
                    Description = cat.Description,
                    Weight = weight--,
                    LocalItemIds = localMediaIds
                });
            }
        }

        _cache.Set(cacheKey, lists, TimeSpan.FromHours(12));

        return lists;
    }

    private class OpenAiChatResponse { public List<Choice> Choices { get; set; } = new(); public Usage Usage { get; set; } = new(); }
    private class Choice { public Message Message { get; set; } = new(); }
    private class Message { public string Content { get; set; } = string.Empty; }
    private class OpenAiEmbedResponse { public List<EmbedData> Data { get; set; } = new(); public Usage Usage { get; set; } = new(); }
    private class EmbedData { public float[] Embedding { get; set; } = Array.Empty<float>(); }
    private class Usage { public int Prompt_Tokens { get; set; } public int Completion_Tokens { get; set; } public int Total_Tokens { get; set; } }
    private class OpenAiCategoryResult { public List<CategoryDto> Categories { get; set; } = new(); }
    private class CategoryDto { public string Title { get; set; } = ""; public string Description { get; set; } = ""; public string Vibe_Keywords { get; set; } = ""; }
}