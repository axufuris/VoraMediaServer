using System.Text.Json;
using Vora.Application.Ai;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Recommendations.Providers;

public class OpenAiRecommendationProvider : IRecommendationProvider, IPluginConnectionTest
{
    public const string ModelSettingType = "model";

    private const int ItemsPerCategory = 15;
    internal const int Categories = 5;
    internal const int RecentTitles = 12;
    internal const int LovedTitles = 8;

    private static readonly List<string> ChatModels = new() { "gpt-4o-mini", "gpt-4o", "gpt-4.1-mini", "gpt-4.1", "gpt-5-nano", "gpt-5-mini", "gpt-5" };
    private static readonly List<string> EmbeddingModels = new() { AiConnection.OpenAiEmbeddingModel };

    private readonly IOpenAiRecommendationRepository _repository;
    private readonly IOpenAiClient _openAi;
    private readonly IAiConnectionTester _tester;

    public string Id => AiConnection.PluginId;
    public string Name => "AI Smart Recommendations";
    public string Description => "Uses an AI model to invent personalized recommendation categories from what each person watches. Also holds the AI connection every AI feature shares: OpenAI, or any server that speaks OpenAI's API, such as Ollama or LM Studio.";
    public string Version => "1.1.0";
    public string Type => "Recommendation";
    public bool IsSystemPlugin => true;
    public bool IsAiPlugin => true;

    // A chat completion and an embeddings request: For You only ever builds
    // these rows in the background, and keeps them twelve hours.
    public bool MakesOutsideCalls => true;

    public OpenAiRecommendationProvider(IOpenAiRecommendationRepository repository, IOpenAiClient openAi, IAiConnectionTester tester)
    {
        _repository = repository;
        _openAi = openAi;
        _tester = tester;
    }

    public Task<PluginConnectionTestResult> TestConnectionAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default) =>
        _tester.TestAsync(settings, cancellationToken);

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto { Key = AiConnection.BaseUrlKey, Label = "Server Address", Type = "text", DefaultValue = AiConnection.OpenAiBaseUrl, Placeholder = "http://host.docker.internal:11434/v1", Description = "Where the AI server answers. Leave it as https://api.openai.com/v1 for OpenAI. For a model you run yourself (Ollama, LM Studio, llama.cpp, vLLM, LocalAI) or another service that speaks OpenAI's API (OpenRouter), enter its address, usually ending in /v1. Vora runs in Docker, so localhost means the Vora container: use host.docker.internal or the computer's network address." },
            new PluginSettingDefinitionDto { Key = AiConnection.ApiKeyKey, Label = "API Key", Type = "password", Description = "For OpenAI: the secret key (sk-...). Create one at https://platform.openai.com/api-keys. Requires an OpenAI account with billing enabled (https://platform.openai.com/account/billing); usage is metered. A server you run yourself usually needs no key: leave it empty." },
            new PluginSettingDefinitionDto { Key = AiConnection.ChatModelKey, Label = "Recommendations Model", Type = ModelSettingType, DefaultValue = AiConnection.OpenAiChatModel, Options = ChatModels, Description = "Model that invents the 'For You' recommendation categories, and the one the other two use when they are left empty. gpt-4o-mini is a good, inexpensive default on OpenAI. On another server, enter the name of a model it has." },
            new PluginSettingDefinitionDto { Key = AiConnection.CollectionsModelKey, Label = "AI Collections Model", Type = ModelSettingType, DefaultValue = AiConnection.OpenAiCollectionsModel, Options = ChatModels, Description = "Model used for AI collection lists and chronological ordering, which benefit from stronger reasoning and up-to-date knowledge. gpt-5 is recommended for best accuracy on OpenAI; gpt-5-mini is a cheaper alternative. A small model you run yourself will invent titles and get orders wrong. Empty uses the recommendations model." },
            new PluginSettingDefinitionDto { Key = AiConnection.PlaylistsModelKey, Label = "AI Playlists Model", Type = ModelSettingType, Options = ChatModels, Description = "Model that describes how each song feels and reads playlist requests. Empty uses the recommendations model." },
            new PluginSettingDefinitionDto { Key = AiConnection.EmbeddingModelKey, Label = "Embedding Model", Type = ModelSettingType, DefaultValue = AiConnection.OpenAiEmbeddingModel, Options = EmbeddingModels, Description = "Model that turns titles and songs into the numbers recommendations and AI playlists search by. It must return 1536 numbers per item or fewer. Changing it means every title and song is read again by the new model, on the next nightly run. On a server other than OpenAI, empty turns recommendations and AI playlists off and leaves collections working." },
            new PluginSettingDefinitionDto { Key = "schedule_time", Label = "Nightly Vector Generation Time", Type = "time", DefaultValue = "02:00", Description = "Time to run the nightly AI vector generation (HH:mm format)." },
            new PluginSettingDefinitionDto { Key = AiConnection.MonthlyTokenLimitKey, Label = "Monthly Token Limit", Type = "number", DefaultValue = "0", Description = "Optional ceiling on total tokens used per calendar month across ALL AI features (recommendations, AI collections, AI playlists). 0 = no limit. When the limit is reached, further AI calls are refused with a clear message until the next month. gpt-4o-mini is inexpensive, but this protects against runaway spend." },
            new PluginSettingDefinitionDto { Key = AiConnection.GuardrailsKey, Label = "Guardrails", Type = "textarea", Description = "Optional house rules added to EVERY AI request across all AI features (recommendations, AI collections & ordering, AI playlists). Use it to steer results — e.g. 'Only suggest titles I could reasonably own', 'Exclude adult content', 'Prefer official English titles', 'When ordering, favour in-universe chronology over release order.' Leave blank for none." }
        };
    }

    // Every call asks the model. The rows are kept by RecommendationManager,
    // which stores them for twelve hours and never calls this while a page
    // waits (docs/for-you.md).
    public async Task<IEnumerable<RecommendationListDto>> GetRecommendationsAsync(Guid profileId, Guid? libraryId, CancellationToken cancellationToken = default)
    {
        var lists = new List<RecommendationListDto>();

        var isEnabledForUser = await _repository.IsAiEnabledForProfileAsync(profileId);
        if (!isEnabledForUser) return lists;

        var embeddingModel = await _openAi.GetEmbeddingModelAsync();
        if (string.IsNullOrEmpty(embeddingModel) || !await _openAi.IsConfiguredAsync()) return lists;

        var taste = await _repository.GetTasteContextAsync(profileId, libraryId, RecentTitles, LovedTitles);
        if (taste.Recent.Count == 0 && taste.Loved.Count == 0) return lists;

        var prompt = Prompt(taste);

        var categories = ParseCategories(await _openAi.CompleteJsonAsync(Id, prompt, cancellationToken, profileId: profileId));
        if (categories.Count == 0) return lists;

        var vectors = await _openAi.EmbedAsync(Id, categories.Select(c => $"{c.Title} {c.Description} {c.Vibe_Keywords}").ToList(), cancellationToken);
        if (vectors == null) return lists;

        for (var i = 0; i < categories.Count && i < vectors.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (vectors[i] is not float[] searchVector) continue;

            var localMediaIds = await _repository.VectorSearchUnwatchedMediaAsync(profileId, libraryId, searchVector, ItemsPerCategory, embeddingModel);

            if (localMediaIds.Any())
            {
                lists.Add(new RecommendationListDto
                {
                    Title = $"AI: {categories[i].Title}",
                    Description = categories[i].Description,
                    // The row's place: the clients sort ascending.
                    Weight = lists.Count + 1,
                    LocalItemIds = localMediaIds
                });
            }
        }

        return lists;
    }

    internal static string Prompt(TasteContext taste)
    {
        var seen = new List<string>();
        if (taste.Recent.Count > 0) seen.Add("They recently watched: " + string.Join(", ", taste.Recent) + ".");
        if (taste.Loved.Count > 0) seen.Add("They rated these highly: " + string.Join(", ", taste.Loved) + ".");

        return $"You are a curator of {taste.Kind}. " + string.Join(" ", seen) +
               $" Invent {Categories} highly specific, creative recommendation category titles for {taste.Kind} (e.g. 'Gritty Heists', 'Cozy Sci-Fi') they would love, each different from the others. " +
               "Output valid JSON ONLY: { \"categories\": [ { \"title\": \"...\", \"description\": \"...\", \"vibe_keywords\": \"...\" } ] }";
    }

    internal static List<CategoryDto> ParseCategories(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<CategoryDto>();

        try
        {
            var parsed = JsonSerializer.Deserialize<OpenAiCategoryResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return (parsed?.Categories ?? new List<CategoryDto>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Title))
                .ToList();
        }
        catch (JsonException)
        {
            return new List<CategoryDto>();
        }
    }

    internal class OpenAiCategoryResult { public List<CategoryDto>? Categories { get; set; } }
    internal class CategoryDto { public string Title { get; set; } = ""; public string Description { get; set; } = ""; public string Vibe_Keywords { get; set; } = ""; }
}
