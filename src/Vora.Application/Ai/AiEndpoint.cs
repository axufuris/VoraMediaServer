using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Vora.Application.Ai;

public class AiEndpoint(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<AiEndpoint> logger) : IAiEndpoint
{
    private static readonly TimeSpan QuirkLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);

    public async Task<AiChatReply> ChatAsync(AiConnection connection, string model, string? systemMessage, string prompt, double? temperature, CancellationToken cancellationToken = default)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            messages.Add(new { role = "system", content = systemMessage });
        }
        messages.Add(new { role = "user", content = prompt });

        var plainKey = $"ai-plain|{connection.BaseUri}|{model}";
        var plain = cache.TryGetValue(plainKey, out _);

        var client = httpClientFactory.CreateClient();
        client.Timeout = connection.ChatTimeout;

        var response = await SendAsync(client, connection, HttpMethod.Post, "chat/completions", ChatPayload(model, messages, temperature, plain), cancellationToken);
        if (!plain && IsRejectedRequest(response.StatusCode))
        {
            var rejection = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();

            response = await SendAsync(client, connection, HttpMethod.Post, "chat/completions", ChatPayload(model, messages, temperature, plain: true), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                cache.Set(plainKey, true, QuirkLifetime);
                logger.LogInformation("{Server} does not accept JSON mode or sampling options for {Model}, so requests to it are sent without them. It answered: {Body}", Name(connection), model, rejection);
            }
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await FailureAsync(connection, response, model, "chat completion", cancellationToken);
            }

            var data = await ReadAsync<ChatResponse>(connection, response, cancellationToken);
            var content = data?.Choices?.FirstOrDefault()?.Message?.Content;
            return new AiChatReply(
                content == null ? null : AiJson.Extract(content),
                data?.Usage?.PromptTokens ?? 0,
                data?.Usage?.CompletionTokens ?? 0,
                data?.Usage?.TotalTokens ?? 0);
        }
    }

    public async Task<AiEmbeddingReply> EmbedAsync(AiConnection connection, string model, IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        var vectors = new float[]?[inputs.Count];
        if (inputs.Count == 0) return new AiEmbeddingReply(vectors, 0, 0, 0);

        var client = httpClientFactory.CreateClient();
        client.Timeout = connection.EmbeddingTimeout;

        var batchSize = Math.Min(connection.EmbeddingBatchSize, inputs.Count);
        var dimensions = 0;
        var promptTokens = 0;
        var totalTokens = 0;

        for (var offset = 0; offset < inputs.Count; offset += batchSize)
        {
            var batch = inputs.Skip(offset).Take(batchSize).ToList();
            var data = await RequestEmbeddingsAsync(client, connection, model, batch, cancellationToken);
            var entries = data.Data ?? new List<EmbeddingEntry>();
            var byPosition = ArrivedWithoutIndexes(entries, batch.Count);

            for (var i = 0; i < entries.Count; i++)
            {
                var index = byPosition ? i : entries[i].Index ?? i;
                if (index < 0 || index >= batch.Count || entries[i].Embedding is not { Length: > 0 } embedding) continue;

                dimensions = Math.Max(dimensions, embedding.Length);
                vectors[offset + index] = Fit(embedding);
            }

            promptTokens += data.Usage?.PromptTokens ?? 0;
            totalTokens += data.Usage?.TotalTokens ?? 0;
        }

        return new AiEmbeddingReply(vectors, dimensions, promptTokens, totalTokens);
    }

    public async Task<AiModelCatalog> ListModelsAsync(AiConnection connection, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = ListTimeout;

        using var response = await SendAsync(client, connection, HttpMethod.Get, "models", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await FailureAsync(connection, response, null, "model list", cancellationToken);
        }

        var names = AiModelNames.Read(await ReadAsync<JsonElement>(connection, response, cancellationToken));
        return new AiModelCatalog(
            names.Where(n => AiModelNames.IsChat(n, connection.IsOpenAi)).ToList(),
            names.Where(AiModelNames.IsEmbedding).ToList());
    }

    public static float[] Fit(float[] vector)
    {
        if (vector.Length >= AiConnection.StoredDimensions) return vector;

        var padded = new float[AiConnection.StoredDimensions];
        vector.CopyTo(padded, 0);
        return padded;
    }

    public static string Name(AiConnection connection) =>
        connection.IsOpenAi ? "OpenAI" : $"The AI server at {connection.Host}";

    private async Task<EmbeddingResponse> RequestEmbeddingsAsync(HttpClient client, AiConnection connection, string model, IReadOnlyList<string> batch, CancellationToken cancellationToken)
    {
        var reduceKey = $"ai-dimensions|{connection.BaseUri}|{model}";
        var reduce = cache.TryGetValue(reduceKey, out _);

        var data = await PostEmbeddingsAsync(client, connection, model, batch, reduce, cancellationToken);
        var longest = Longest(data);
        if (longest <= AiConnection.StoredDimensions) return data;

        if (!reduce)
        {
            EmbeddingResponse? reduced = null;
            try
            {
                reduced = await PostEmbeddingsAsync(client, connection, model, batch, reduce: true, cancellationToken);
            }
            catch (AiEndpointException)
            {
            }

            if (reduced != null && Longest(reduced) <= AiConnection.StoredDimensions)
            {
                cache.Set(reduceKey, true, QuirkLifetime);
                return reduced;
            }
        }

        throw new AiEndpointException(
            $"The embedding model \"{model}\" returns {longest:N0} numbers per item and Vora stores at most {AiConnection.StoredDimensions:N0}. Choose a smaller embedding model.");
    }

    private async Task<EmbeddingResponse> PostEmbeddingsAsync(HttpClient client, AiConnection connection, string model, IReadOnlyList<string> batch, bool reduce, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object> { ["model"] = model, ["input"] = batch };
        if (reduce) payload["dimensions"] = AiConnection.StoredDimensions;

        using var response = await SendAsync(client, connection, HttpMethod.Post, "embeddings", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await FailureAsync(connection, response, model, "embedding", cancellationToken);
        }

        return await ReadAsync<EmbeddingResponse>(connection, response, cancellationToken) ?? new EmbeddingResponse();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, AiConnection connection, HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, connection.Endpoint(path));
        if (connection.ApiKey != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
        }
        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiEndpointException(Unreachable(connection), innerException: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiEndpointException($"{Name(connection)} took longer than {client.Timeout.TotalMinutes:0.##} minutes to answer.", innerException: ex);
        }
    }

    private static async Task<T?> ReadAsync<T>(AiConnection connection, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new AiEndpointException($"{Name(connection)} sent an answer Vora could not read. Check that the address ends where its OpenAI-compatible API starts, usually /v1.", innerException: ex);
        }
    }

    private async Task<AiEndpointException> FailureAsync(AiConnection connection, HttpResponseMessage response, string? model, string operation, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("{Server} refused a {Operation} request ({Status}) for model {Model}: {Body}", Name(connection), operation, status, model, body);
        return new AiEndpointException(FailureMessage(connection, response.StatusCode, model), status);
    }

    private static string FailureMessage(AiConnection connection, HttpStatusCode status, string? model)
    {
        var code = (int)status;
        if (connection.IsOpenAi)
        {
            return $"OpenAI request failed ({code}). Check the API key and account billing.";
        }

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{Name(connection)} rejected the API key ({code}).",
            HttpStatusCode.NotFound when model != null => $"{Name(connection)} has no model named \"{model}\", or the address is wrong ({code}).",
            HttpStatusCode.NotFound => $"{Name(connection)} has no OpenAI-compatible API at that address ({code}). The address usually ends in /v1.",
            _ => $"{Name(connection)} refused the request ({code})."
        };
    }

    private static string Unreachable(AiConnection connection)
    {
        if (connection.IsOpenAi) return "Couldn't reach OpenAI. Check the server's internet connection.";

        var message = $"Couldn't reach the AI server at {connection.Host}.";
        return connection.BaseUri is { IsLoopback: true }
            ? message + " Vora runs in Docker, where localhost is the Vora container itself. Use host.docker.internal or the computer's network address instead."
            : message + " Check the address and that the server is running and accepts connections from other computers.";
    }

    private static Dictionary<string, object> ChatPayload(string model, List<object> messages, double? temperature, bool plain)
    {
        var payload = new Dictionary<string, object> { ["model"] = model, ["messages"] = messages };
        if (plain) return payload;

        payload["response_format"] = new { type = "json_object" };
        if (AiModelNames.IsReasoning(model))
        {
            payload["reasoning_effort"] = "low";
        }
        else if (temperature.HasValue)
        {
            payload["temperature"] = temperature.Value;
        }
        return payload;
    }

    private static bool IsRejectedRequest(HttpStatusCode status) =>
        status is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;

    private static bool ArrivedWithoutIndexes(List<EmbeddingEntry> entries, int requested) =>
        entries.Count == requested
        && (entries.Any(e => e.Index == null) || (entries.Count > 1 && entries.All(e => e.Index == 0)));

    private static int Longest(EmbeddingResponse response) =>
        (response.Data ?? new List<EmbeddingEntry>()).Select(e => e.Embedding?.Length ?? 0).DefaultIfEmpty(0).Max();

    private sealed class EmbeddingResponse
    {
        public List<EmbeddingEntry>? Data { get; set; }
        public UsageInfo? Usage { get; set; }
    }

    private sealed class EmbeddingEntry
    {
        public float[]? Embedding { get; set; }
        public int? Index { get; set; }
    }

    private sealed class ChatResponse
    {
        public List<Choice>? Choices { get; set; }
        public UsageInfo? Usage { get; set; }
    }

    private sealed class Choice
    {
        public MessageContent? Message { get; set; }
    }

    private sealed class MessageContent
    {
        public string? Content { get; set; }
    }

    private sealed class UsageInfo
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
    }
}
