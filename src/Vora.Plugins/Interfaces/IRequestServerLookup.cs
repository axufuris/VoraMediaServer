namespace Vora.Plugins.Interfaces;

public interface IRequestServerLookup
{
    Task<IReadOnlyList<RequestServerCredentialsDto>> GetCalendarServersAsync(string providerId, CancellationToken ct = default);
}

public sealed class RequestServerCredentialsDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
}
