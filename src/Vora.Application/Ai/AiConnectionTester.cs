using Vora.Application.Ai.ViewModels;
using Vora.Application.Settings;
using Vora.Plugins.Dtos;

namespace Vora.Application.Ai;

public interface IAiConnectionTester
{
    Task<PluginConnectionTestResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default);
    Task<AiModelsVM> ListModelsAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default);
    Task<AiStatusVM> GetStatusAsync();
}

public class AiConnectionTester(IAiEndpoint endpoint, ISystemSettingsRepository settingsRepository) : IAiConnectionTester
{
    private const string ProbeText = "Vora";

    public async Task<AiStatusVM> GetStatusAsync()
    {
        var connection = await AiConnection.LoadAsync(settingsRepository);
        return new AiStatusVM
        {
            Configured = connection.CanChat,
            Embeddings = connection.CanEmbed,
            IsOpenAi = connection.IsOpenAi,
            Host = connection.Host
        };
    }

    public async Task<AiModelsVM> ListModelsAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        var connection = new AiConnection(settings);
        var problem = Incomplete(connection);
        if (problem != null) return new AiModelsVM { Error = problem };

        try
        {
            var catalog = await endpoint.ListModelsAsync(connection, cancellationToken);
            return new AiModelsVM { ChatModels = catalog.ChatModels.ToList(), EmbeddingModels = catalog.EmbeddingModels.ToList() };
        }
        catch (AiEndpointException ex)
        {
            return new AiModelsVM { Error = ex.Message };
        }
    }

    public async Task<PluginConnectionTestResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        var connection = new AiConnection(settings);
        var problem = Incomplete(connection);
        if (problem != null) return PluginConnectionTestResult.Fail(problem);

        AiModelCatalog catalog;
        try
        {
            catalog = await endpoint.ListModelsAsync(connection, cancellationToken);
        }
        catch (AiEndpointException ex)
        {
            return PluginConnectionTestResult.Fail(connection.IsOpenAi ? OpenAiRefusal(ex) : ex.Message);
        }

        var known = catalog.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chosen = new[]
            {
                connection.ChatModel(),
                connection.ChatModel(AiConnection.CollectionsModelKey),
                connection.ChatModel(AiConnection.PlaylistsModelKey),
                connection.EmbeddingModel
            }
            .Where(m => m != null)
            .Select(m => m ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missing = known.Count == 0 ? null : chosen.FirstOrDefault(m => !known.Contains(m));
        if (missing != null)
        {
            return PluginConnectionTestResult.Fail($"Connected, but {Server(connection)} has no model named \"{missing}\".");
        }

        if (connection.ChatModel() == null)
        {
            return PluginConnectionTestResult.Ok($"Connected. {Count(known.Count)} found. Choose which to use below.");
        }

        var embeddingModel = connection.EmbeddingModel;
        if (embeddingModel == null)
        {
            return PluginConnectionTestResult.Ok("Connected. No embedding model is chosen, so recommendations and AI playlists stay off.");
        }

        if (connection.IsOpenAi && AiConnection.IsLegacyStamp(connection.EmbeddingStamp))
        {
            return PluginConnectionTestResult.Ok("OpenAI accepted the API key.");
        }

        try
        {
            var reply = await endpoint.EmbedAsync(connection, embeddingModel, new[] { ProbeText }, cancellationToken);
            if (reply.Vectors.Count == 0 || reply.Vectors[0] == null)
            {
                return PluginConnectionTestResult.Fail($"Connected, but \"{embeddingModel}\" returned nothing. Check that it is an embedding model.");
            }
        }
        catch (AiEndpointException ex)
        {
            return PluginConnectionTestResult.Fail($"Connected, but the embedding model did not work. {ex.Message}");
        }

        return PluginConnectionTestResult.Ok(connection.IsOpenAi ? "OpenAI accepted the API key." : "Connected. The chosen models are ready.");
    }

    private static string? Incomplete(AiConnection connection)
    {
        if (connection.BaseUri == null)
        {
            return "Enter the server address as http:// or https://, for example http://host.docker.internal:11434/v1.";
        }
        if (connection.IsOpenAi && connection.ApiKey == null)
        {
            return "Enter an API key first.";
        }
        return null;
    }

    private static string OpenAiRefusal(AiEndpointException ex) => ex.StatusCode switch
    {
        401 or 403 => "OpenAI rejected the API key.",
        429 => "OpenAI accepted the key but refused the request: check that the account has billing set up and credit left.",
        int status => $"Unexpected response from OpenAI (HTTP {status}).",
        null => ex.Message
    };

    private static string Server(AiConnection connection) =>
        connection.IsOpenAi ? "OpenAI" : $"the server at {connection.Host}";

    private static string Count(int models) =>
        models == 1 ? "1 model" : $"{models:N0} models";
}
