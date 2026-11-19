using Vora.Domain.Entities.Webhooks;

namespace Vora.Application.Webhooks;

public interface IWebhookConnectionRepository
{
    Task<List<WebhookConnection>> GetAllAsync();

    Task<WebhookConnection?> GetAsync(Guid id);

    Task AddAsync(WebhookConnection connection);

    Task UpdateAsync(WebhookConnection connection);

    Task<bool> DeleteAsync(Guid id);
}

public interface IWebhookSecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}

public interface IArrRootFolderClient
{
    Task<IReadOnlyList<string>> GetRootFoldersAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default);
}
