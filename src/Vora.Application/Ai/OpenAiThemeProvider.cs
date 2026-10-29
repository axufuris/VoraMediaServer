using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Application.Collections;
using Vora.Domain.Entities.Collections;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Ai;

public class OpenAiThemeProvider(IOpenAiClient openAi, ICollectionThemeRepository themes, ILogger<OpenAiThemeProvider> logger)
    : ICollectionSyncProvider, ILibraryCollectionSyncProvider
{
    public const string ProviderId = "openai_theme";
    public const int CandidateCount = 150;
    public const int JudgedPerRequest = 60;
    private const int OverviewLength = 240;
    private const int MaxKeywords = 8;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string Id => ProviderId;
    public string Name => "AI Theme";
    public string Version => "1.0.0";
    public string Description => "Fills a collection with the movies and shows in your library that fit a theme or occasion, such as Halloween, Valentine's Day or Mother's Day. Titles TMDB tags with the theme go in directly; the titles closest to it by their descriptions are checked once by the AI, and the answers are kept, so later syncs only ask about new titles.";
    public bool IsSystemPlugin => true;
    public bool IsAiPlugin => true;
    public string Type => "Collection_Sync";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public string ExternalIdLabel => "Theme or occasion";
    public string ExternalIdPlaceholder => "e.g., Halloween, Valentine's Day, Mother's Day, Christmas";

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    public Task<List<CollectionSyncItemDto>> FetchItemsAsync(string externalId) =>
        Task.FromResult(new List<CollectionSyncItemDto>());

    public async Task<string?> GenerateDescriptionAsync(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return null;

        var json = await openAi.CompleteJsonAsync(Id, BuildDescriptionPrompt(externalId.Trim()), temperature: 0.3, modelSettingKey: "collections_model");
        return Parse<DescriptionResult>(json)?.Description is { Length: > 0 } description ? description.Trim() : null;
    }

    public async Task<IReadOnlyCollection<Guid>> FindInLibraryAsync(LibraryCollectionSyncRequest request, CancellationToken cancellationToken)
    {
        var theme = request.Theme.Trim();
        if (theme.Length == 0) return Array.Empty<Guid>();

        var profile = await ProfileAsync(request.CollectionId, theme, cancellationToken);
        if (profile == null) return Array.Empty<Guid>();

        var members = new HashSet<Guid>(profile.Keywords.Count > 0
            ? await themes.FindByKeywordsAsync(profile.Keywords, request.LibraryId)
            : new List<Guid>());

        var verdicts = await themes.GetVerdictsAsync(request.CollectionId);

        if (profile.Embedding is { } vector && profile.EmbeddingModel is { } model)
        {
            var nearest = await themes.FindNearestAsync(vector, model, request.LibraryId, CandidateCount);
            var unjudged = nearest.Where(id => !members.Contains(id) && !verdicts.ContainsKey(id)).ToList();
            if (unjudged.Count > 0)
            {
                var judged = await JudgeAsync(theme, profile.Profile, unjudged, cancellationToken);
                if (judged.Count > 0)
                {
                    await themes.SaveVerdictsAsync(request.CollectionId, judged);
                    foreach (var (id, belongs) in judged) verdicts[id] = belongs;
                }
            }
        }

        members.UnionWith(verdicts.Where(v => v.Value).Select(v => v.Key));
        return members;
    }

    private async Task<CollectionTheme?> ProfileAsync(Guid collectionId, string theme, CancellationToken cancellationToken)
    {
        var stored = await themes.GetAsync(collectionId);
        var embeddingModel = await openAi.GetEmbeddingModelAsync();

        if (stored != null && stored.Theme == theme)
        {
            if (string.IsNullOrEmpty(embeddingModel) || stored.EmbeddingModel == embeddingModel) return stored;

            stored.Embedding = await EmbedAsync(stored.Profile, cancellationToken);
            stored.EmbeddingModel = stored.Embedding == null ? null : embeddingModel;
            await themes.SaveAsync(stored, forgetVerdicts: false);
            return stored;
        }

        var parsed = Parse<ProfileResult>(await openAi.CompleteJsonAsync(Id, BuildProfilePrompt(theme), cancellationToken, temperature: 0.3, modelSettingKey: "collections_model"));
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Profile))
        {
            logger.LogWarning("The AI gave no profile for the theme '{Theme}'; the collection is left as it is.", theme);
            return null;
        }

        var embedding = string.IsNullOrEmpty(embeddingModel) ? null : await EmbedAsync(parsed.Profile, cancellationToken);
        var profile = new CollectionTheme
        {
            CollectionId = collectionId,
            Theme = theme,
            Profile = parsed.Profile.Trim(),
            Keywords = Keywords(parsed.Keywords),
            Embedding = embedding,
            EmbeddingModel = embedding == null ? null : embeddingModel,
        };
        await themes.SaveAsync(profile, forgetVerdicts: true);
        return profile;
    }

    private async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var vectors = await openAi.EmbedAsync(Id, new[] { text }, cancellationToken);
        return vectors is { Count: > 0 } ? vectors[0] : null;
    }

    private async Task<Dictionary<Guid, bool>> JudgeAsync(string theme, string profile, List<Guid> ids, CancellationToken cancellationToken)
    {
        var candidates = await themes.GetCandidatesAsync(ids);
        var verdicts = new Dictionary<Guid, bool>();

        foreach (var batch in candidates.Chunk(JudgedPerRequest))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = Parse<JudgeResult>(await openAi.CompleteJsonAsync(Id, BuildJudgePrompt(theme, profile, batch), cancellationToken, temperature: 0.1, modelSettingKey: "collections_model"));
            if (parsed?.Belongs == null) continue;

            var belonging = parsed.Belongs.ToHashSet();
            for (var i = 0; i < batch.Length; i++)
            {
                verdicts[batch[i].Id] = belonging.Contains(i);
            }
        }

        return verdicts;
    }

    private static List<string> Keywords(List<string>? keywords) =>
        (keywords ?? new List<string>())
            .Select(k => k.Trim().ToLowerInvariant())
            .Where(k => k.Length > 0)
            .Distinct()
            .Take(MaxKeywords)
            .ToList();

    private static string BuildDescriptionPrompt(string theme) =>
        $"Write a concise, engaging 1-2 sentence description of a media library collection of movies and TV shows for: \"{theme}\". " +
        "Say what the collection is for, not how to watch it. No preamble, no title prefix, just the description prose. " +
        "Return ONLY valid JSON of the form {\"description\": \"...\"}.";

    private static string BuildProfilePrompt(string theme) =>
        $"A home media library is getting a themed collection of movies and TV shows for: \"{theme}\".\n" +
        "Return ONLY valid JSON of the form {\"profile\": \"...\", \"keywords\": [\"...\"]}.\n" +
        "profile: 3-4 sentences describing the kind of movie or show that belongs in it, written the way a plot summary and its genres read: " +
        "what such a title is about, when and where it is set, the events, traditions and imagery it features. Describe titles that are ABOUT " +
        "the theme or set during it, not ones that only share a genre or a mood with it.\n" +
        $"keywords: up to {MaxKeywords} keyword names as TMDB writes them (lowercase, e.g. \"halloween\", \"christmas\", \"valentine's day\") " +
        "that TMDB attaches to titles about this theme. Only keywords that mark a title as being about it; never a broad genre or mood " +
        "word such as \"horror\", \"romance\" or \"family\".";

    private static string BuildJudgePrompt(string theme, string profile, IReadOnlyList<ThemeCandidate> batch)
    {
        var lines = new StringBuilder();
        for (var i = 0; i < batch.Count; i++)
        {
            var title = batch[i];
            lines.Append(i).Append(". ").Append(title.Title);
            if (title.Year.HasValue) lines.Append(" (").Append(title.Year.Value).Append(')');
            lines.Append(" · ").Append(title.IsShow ? "show" : "movie");
            if (title.Genres.Count > 0) lines.Append(" · ").Append(string.Join(", ", title.Genres));
            if (!string.IsNullOrWhiteSpace(title.Overview)) lines.Append(" — ").Append(Shorten(title.Overview));
            lines.Append('\n');
        }

        return
            $"A themed collection in a home media library is for: \"{theme}\". {profile}\n" +
            "Below are titles from the library, one per line, each with an index. Decide which belong. A title belongs when it is about " +
            "the theme or occasion, or a substantial part of it takes place during it or revolves around it. Do NOT include a title only " +
            "because it shares a genre or a mood with the theme (a horror film that has nothing to do with Halloween does not belong in a " +
            "Halloween collection). Use what you know about each title as well as the line given; leave out any title you do not recognize " +
            "and cannot place from its line.\n" +
            lines +
            "Return ONLY valid JSON of the form {\"belongs\": [0, 3, 7]} listing the indexes that belong, or {\"belongs\": []} if none do.";
    }

    private static string Shorten(string text)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= OverviewLength ? flat : flat[..OverviewLength].TrimEnd() + "…";
    }

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class DescriptionResult
    {
        public string? Description { get; set; }
    }

    private sealed class ProfileResult
    {
        public string? Profile { get; set; }
        public List<string>? Keywords { get; set; }
    }

    private sealed class JudgeResult
    {
        public List<int>? Belongs { get; set; }
    }
}
