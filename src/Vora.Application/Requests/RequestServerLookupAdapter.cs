using Vora.Plugins.Interfaces;

namespace Vora.Application.Requests;

public sealed class RequestServerLookupAdapter : IRequestServerLookup
{
    private readonly IRequestRepository _repository;

    public RequestServerLookupAdapter(IRequestRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<RequestServerCredentialsDto>> GetCalendarServersAsync(string providerId, CancellationToken ct = default)
    {
        var all = await _repository.GetAllServersAsync();
        return all
            .Where(s => s.IsEnabled
                && s.ProvidesReleaseCalendar
                && string.Equals(s.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .Select(s => new RequestServerCredentialsDto
            {
                Id = s.Id,
                Name = s.Name,
                BaseUrl = BuildBaseUrl(s.UseSsl, s.Hostname, s.Port, s.UrlBase),
                ApiKey = s.ApiKey
            })
            .ToList();
    }

    private static string BuildBaseUrl(bool useSsl, string hostname, int port, string urlBase)
    {
        var scheme = useSsl ? "https" : "http";
        var trimmedBase = (urlBase ?? string.Empty).Trim('/');
        var portSegment = port > 0 ? $":{port}" : string.Empty;
        var basePath = string.IsNullOrEmpty(trimmedBase) ? string.Empty : $"/{trimmedBase}";
        return $"{scheme}://{hostname}{portSegment}{basePath}";
    }
}
