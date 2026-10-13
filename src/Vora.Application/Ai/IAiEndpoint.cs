namespace Vora.Application.Ai;

public interface IAiEndpoint
{
    Task<AiChatReply> ChatAsync(AiConnection connection, string model, string? systemMessage, string prompt, double? temperature, CancellationToken cancellationToken = default);

    Task<AiEmbeddingReply> EmbedAsync(AiConnection connection, string model, IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);

    Task<AiModelCatalog> ListModelsAsync(AiConnection connection, CancellationToken cancellationToken = default);
}

public sealed record AiChatReply(string? Content, int PromptTokens, int CompletionTokens, int TotalTokens);

public sealed record AiEmbeddingReply(IReadOnlyList<float[]?> Vectors, int Dimensions, int PromptTokens, int TotalTokens);

public sealed record AiModelCatalog(IReadOnlyList<string> ChatModels, IReadOnlyList<string> EmbeddingModels)
{
    public IEnumerable<string> All => ChatModels.Concat(EmbeddingModels);
}

public sealed class AiEndpointException : InvalidOperationException
{
    public AiEndpointException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
