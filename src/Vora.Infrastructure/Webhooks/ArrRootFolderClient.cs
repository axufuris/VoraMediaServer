using System.Text.Json;
using Vora.Application.Webhooks;

namespace Vora.Infrastructure.Webhooks;

public class ArrRootFolderClient(HttpClient httpClient) : IArrRootFolderClient
{
    public async Task<IReadOnlyList<string>> GetRootFoldersAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/api/v3/rootfolder");
        request.Headers.Add("X-Api-Key", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The server did not answer with a list of folders.");
        }

        return document.RootElement.EnumerateArray()
            .Select(e => e.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString() : null)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();
    }
}
