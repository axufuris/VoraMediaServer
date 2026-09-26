namespace Vora.Application.Ai;

public interface IOpenAiClient
{
    // True when an OpenAI API key is configured (the plugin can offer AI features).
    Task<bool> IsConfiguredAsync();

    // Sends a JSON-mode chat completion for the given calling plugin id and
    // returns the assistant's content string. Returns null when no API key is
    // configured. Throws InvalidOperationException when the configured monthly
    // token limit has been reached. Usage is logged against the calling plugin.
    Task<string?> CompleteJsonAsync(string pluginId, string prompt, CancellationToken cancellationToken = default, double? temperature = null, string? modelSettingKey = null);

    // text-embedding-3-small vectors for the inputs, in input order; an entry
    // the response didn't cover is null. Null overall when no key is
    // configured. Same monthly limit and usage logging as chat.
    Task<IReadOnlyList<float[]?>?> EmbedAsync(string pluginId, IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);
}
