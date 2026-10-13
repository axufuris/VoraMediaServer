using Vora.Application.Settings;
using Vora.Domain.Entities.Ai;

namespace Vora.Application.Ai;

public class OpenAiClient(
    IAiEndpoint endpoint,
    ISystemSettingsRepository settings,
    IAiUsageRepository usage) : IOpenAiClient
{
    public async Task<bool> IsConfiguredAsync() =>
        (await AiConnection.LoadAsync(settings)).CanChat;

    public async Task<string?> GetEmbeddingModelAsync()
    {
        var connection = await AiConnection.LoadAsync(settings);
        return connection.CanEmbed ? connection.EmbeddingStamp : null;
    }

    public async Task<int> GetParallelismAsync() =>
        (await AiConnection.LoadAsync(settings)).Parallelism;

    public async Task<string?> CompleteJsonAsync(string pluginId, string prompt, CancellationToken cancellationToken = default, double? temperature = null, string? modelSettingKey = null, Guid? profileId = null)
    {
        var connection = await AiConnection.LoadAsync(settings);
        var model = connection.ChatModel(modelSettingKey);
        if (!connection.IsUsable || model == null)
        {
            return null;
        }

        await EnsureWithinMonthlyLimitAsync(connection);

        var reply = await endpoint.ChatAsync(connection, model, connection.Guardrails, prompt, temperature, cancellationToken);
        if (reply.Content == null)
        {
            return null;
        }

        await usage.LogAiUsageAsync(new AiUsageLog
        {
            PluginId = pluginId,
            ModelUsed = model,
            PromptTokens = reply.PromptTokens,
            CompletionTokens = reply.CompletionTokens,
            TotalTokens = reply.TotalTokens,
            ProfileId = profileId
        });

        return reply.Content;
    }

    public async Task<IReadOnlyList<float[]?>?> EmbedAsync(string pluginId, IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) return Array.Empty<float[]?>();

        var connection = await AiConnection.LoadAsync(settings);
        var model = connection.EmbeddingModel;
        if (!connection.IsUsable || model == null) return null;

        await EnsureWithinMonthlyLimitAsync(connection);

        var reply = await endpoint.EmbedAsync(connection, model, inputs, cancellationToken);

        await usage.LogAiUsageAsync(new AiUsageLog
        {
            PluginId = pluginId,
            ModelUsed = model,
            PromptTokens = reply.PromptTokens,
            TotalTokens = reply.TotalTokens
        });

        return reply.Vectors;
    }

    private async Task EnsureWithinMonthlyLimitAsync(AiConnection connection)
    {
        var limit = connection.MonthlyTokenLimit;
        if (limit <= 0) return;

        var used = await usage.GetMonthlyTokenUsageAsync();
        if (used >= limit)
        {
            throw new InvalidOperationException($"AI monthly token limit reached ({used:N0} / {limit:N0} tokens). Raise 'Monthly Token Limit' in AI Settings or wait until next month.");
        }
    }
}
