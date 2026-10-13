using Vora.Application.Settings;

namespace Vora.Application.Ai;

public sealed class AiConnection
{
    public const string PluginId = "openai_recommendations";

    public const string BaseUrlKey = "base_url";
    public const string ApiKeyKey = "api_key";
    public const string ChatModelKey = "chat_model";
    public const string CollectionsModelKey = "collections_model";
    public const string PlaylistsModelKey = "music_playlists_model";
    public const string EmbeddingModelKey = "embedding_model";
    public const string MonthlyTokenLimitKey = "monthly_token_limit";
    public const string GuardrailsKey = "guardrails";

    public const string OpenAiBaseUrl = "https://api.openai.com/v1";
    public const string OpenAiHost = "api.openai.com";
    public const string OpenAiChatModel = "gpt-4o-mini";
    public const string OpenAiCollectionsModel = "gpt-5";
    public const string OpenAiEmbeddingModel = "text-embedding-3-small";

    public const int StoredDimensions = 1536;
    public const int ModelStampLength = 64;

    private static readonly string[] EndpointSuffixes = { "/chat/completions", "/embeddings", "/models" };

    private readonly IReadOnlyDictionary<string, string> _values;

    public AiConnection(IReadOnlyDictionary<string, string>? values)
    {
        _values = values ?? new Dictionary<string, string>();
        BaseUri = ParseBaseUrl(Value(BaseUrlKey));
        ApiKey = Value(ApiKeyKey);
        IsOpenAi = BaseUri != null && string.Equals(BaseUri.Host, OpenAiHost, StringComparison.OrdinalIgnoreCase);
    }

    public Uri? BaseUri { get; }
    public string? ApiKey { get; }
    public bool IsOpenAi { get; }

    public bool IsUsable => BaseUri != null && (!IsOpenAi || ApiKey != null);
    public bool CanChat => IsUsable && ChatModel() != null;
    public bool CanEmbed => IsUsable && EmbeddingModel != null;

    public string Host => BaseUri == null ? string.Empty : BaseUri.IsDefaultPort ? BaseUri.Host : $"{BaseUri.Host}:{BaseUri.Port}";

    public string? EmbeddingModel => Value(EmbeddingModelKey) ?? (IsOpenAi ? OpenAiEmbeddingModel : null);
    public string? EmbeddingStamp => Stamp(EmbeddingModel);
    public string? Guardrails => Value(GuardrailsKey);
    public long MonthlyTokenLimit => long.TryParse(Value(MonthlyTokenLimitKey), out var limit) && limit > 0 ? limit : 0;

    public TimeSpan ChatTimeout => IsOpenAi ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(15);
    public TimeSpan EmbeddingTimeout => IsOpenAi ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(10);
    public int EmbeddingBatchSize => IsOpenAi ? int.MaxValue : 32;
    public int Parallelism => IsOpenAi ? 6 : 1;

    public string? ChatModel(string? featureKey = null)
    {
        var chosen = string.IsNullOrWhiteSpace(featureKey) ? null : Value(featureKey);
        return chosen ?? Value(ChatModelKey) ?? (IsOpenAi ? OpenAiChatModel : null);
    }

    public Uri Endpoint(string path)
    {
        if (BaseUri == null) throw new InvalidOperationException("The AI server address is not a valid http or https address.");
        return new Uri($"{BaseUri.AbsoluteUri.TrimEnd('/')}/{path.TrimStart('/')}");
    }

    public static async Task<AiConnection> LoadAsync(ISystemSettingsRepository settings) =>
        new(await settings.GetAllPluginSettingsAsync(PluginId));

    public static string? Stamp(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var trimmed = model.Trim();
        return trimmed.Length <= ModelStampLength ? trimmed : trimmed[..ModelStampLength];
    }

    public static bool IsLegacyStamp(string? stamp) =>
        string.Equals(stamp, OpenAiEmbeddingModel, StringComparison.Ordinal);

    public static Uri? ParseBaseUrl(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? OpenAiBaseUrl : value.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrWhiteSpace(uri.Host)) return null;

        var path = uri.AbsolutePath.TrimEnd('/');
        foreach (var suffix in EndpointSuffixes)
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length];
                break;
            }
        }
        if (path.Length == 0) path = "/v1";

        return new UriBuilder(uri.Scheme, uri.Host, uri.Port, path).Uri;
    }

    private string? Value(string key) =>
        _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
