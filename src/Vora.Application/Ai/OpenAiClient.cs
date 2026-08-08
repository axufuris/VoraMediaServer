using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Domain.Entities.Ai;

namespace Vora.Application.Ai;

public class OpenAiClient(
    IHttpClientFactory httpClientFactory,
    ISystemSettingsRepository settings,
    IAiUsageRepository usage,
    ILogger<OpenAiClient> logger) : IOpenAiClient
{
    // The api key / model / limit live on the recommendations plugin, which is
    // the established home for the OpenAI credentials; every AI feature shares it.
    private const string KeyPluginId = "openai_recommendations";

    public async Task<bool> IsConfiguredAsync()
        => !string.IsNullOrWhiteSpace(await settings.GetPluginSettingAsync(KeyPluginId, "api_key"));

    public async Task<string?> CompleteJsonAsync(string pluginId, string prompt, CancellationToken cancellationToken = default)
    {
        var apiKey = await settings.GetPluginSettingAsync(KeyPluginId, "api_key");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var limitStr = await settings.GetPluginSettingAsync(KeyPluginId, "monthly_token_limit");
        if (long.TryParse(limitStr, out var limit) && limit > 0)
        {
            var used = await usage.GetMonthlyTokenUsageAsync();
            if (used >= limit)
            {
                throw new InvalidOperationException($"AI monthly token limit reached ({used:N0} / {limit:N0} tokens). Raise 'Monthly Token Limit' in the OpenAI plugin settings or wait until next month.");
            }
        }

        var model = await settings.GetPluginSettingAsync(KeyPluginId, "chat_model");
        if (string.IsNullOrWhiteSpace(model)) model = "gpt-4o-mini";

        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model,
            messages = new[] { new { role = "user", content = prompt } },
            response_format = new { type = "json_object" }
        });

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("OpenAI chat completion failed ({Status}) for plugin {PluginId}: {Body}", (int)response.StatusCode, pluginId, body);
            throw new InvalidOperationException($"OpenAI request failed ({(int)response.StatusCode}). Check the API key and account billing.");
        }

        var data = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken);
        if (data?.Choices == null || data.Choices.Count == 0)
        {
            return null;
        }

        await usage.LogAiUsageAsync(new AiUsageLog
        {
            PluginId = pluginId,
            ModelUsed = model,
            PromptTokens = data.Usage?.PromptTokens ?? 0,
            CompletionTokens = data.Usage?.CompletionTokens ?? 0,
            TotalTokens = data.Usage?.TotalTokens ?? 0
        });

        return data.Choices[0].Message?.Content;
    }

    private class ChatResponse
    {
        public List<Choice> Choices { get; set; } = new();
        public UsageInfo? Usage { get; set; }
    }

    private class Choice
    {
        public MessageContent? Message { get; set; }
    }

    private class MessageContent
    {
        public string? Content { get; set; }
    }

    private class UsageInfo
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
    }
}
