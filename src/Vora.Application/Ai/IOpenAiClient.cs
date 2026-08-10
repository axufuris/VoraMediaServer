namespace Vora.Application.Ai;

public interface IOpenAiClient
{
    // True when an OpenAI API key is configured (the plugin can offer AI features).
    Task<bool> IsConfiguredAsync();

    // Sends a JSON-mode chat completion for the given calling plugin id and
    // returns the assistant's content string. Returns null when no API key is
    // configured. Throws InvalidOperationException when the configured monthly
    // token limit has been reached. Usage is logged against the calling plugin.
    Task<string?> CompleteJsonAsync(string pluginId, string prompt, CancellationToken cancellationToken = default, double? temperature = null);
}
