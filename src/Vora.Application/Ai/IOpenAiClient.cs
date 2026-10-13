namespace Vora.Application.Ai;

public interface IOpenAiClient
{
    // True when the saved AI connection can answer a chat request: an API key
    // for OpenAI, or an address and a chosen model for any other server.
    Task<bool> IsConfiguredAsync();

    // The name stored beside every vector the current embedding model makes, or
    // null when there is no embedding model to use. Vectors from two models
    // cannot be compared, so a search only reads the ones carrying this name.
    Task<string?> GetEmbeddingModelAsync();

    // How many requests the server can be sent at once.
    Task<int> GetParallelismAsync();

    // Sends a JSON-mode chat completion for the given calling plugin id and
    // returns the assistant's content string. Returns null when the connection
    // is not configured. Throws InvalidOperationException when the configured
    // monthly token limit has been reached. Usage is logged against the calling
    // plugin.
    Task<string?> CompleteJsonAsync(string pluginId, string prompt, CancellationToken cancellationToken = default, double? temperature = null, string? modelSettingKey = null, Guid? profileId = null);

    // Vectors from the embedding model for the inputs, in input order and
    // always AiConnection.StoredDimensions long; an entry the response didn't
    // cover is null. Null overall when there is no embedding model to use. Same
    // monthly limit and usage logging as chat.
    Task<IReadOnlyList<float[]?>?> EmbedAsync(string pluginId, IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);
}
