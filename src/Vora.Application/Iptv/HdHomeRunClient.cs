using Vora.Domain.Entities.Iptv;

namespace Vora.Application.Iptv;

public sealed record HdHomeRunStatus(HdHomeRunDevice? Device, IReadOnlyList<HdHomeRunTuner> Tuners);

public interface IHdHomeRunClient
{
    Task<HdHomeRunStatus> GetStatusAsync(IptvPlaylist playlist, CancellationToken cancellationToken = default);
    Task<string> GetDeviceAuthAsync(IptvPlaylist playlist, CancellationToken cancellationToken = default);
}

public sealed class HdHomeRunClient : IHdHomeRunClient
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(8);

    private readonly IHttpClientFactory _httpClientFactory;

    public HdHomeRunClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HdHomeRunStatus> GetStatusAsync(IptvPlaylist playlist, CancellationToken cancellationToken = default)
    {
        var address = AddressOf(playlist);

        HdHomeRunDevice? device = null;
        try
        {
            device = HdHomeRunParser.ParseDevice(await GetStringAsync($"{address}/{HdHomeRunParser.DiscoverPath}", cancellationToken));
        }
        catch (IptvSourceException ex) when (ex.InnerException is null)
        {
        }

        var tuners = HdHomeRunParser.ParseStatus(await GetStringAsync($"{address}/{HdHomeRunParser.StatusPath}", cancellationToken));
        return new HdHomeRunStatus(device, tuners);
    }

    public async Task<string> GetDeviceAuthAsync(IptvPlaylist playlist, CancellationToken cancellationToken = default)
    {
        var json = await GetStringAsync($"{AddressOf(playlist)}/{HdHomeRunParser.DiscoverPath}", cancellationToken);
        return HdHomeRunParser.DeviceAuth(json)
            ?? throw new IptvSourceException("The HDHomeRun gave no authorization for its guide. Its firmware may be too old to use Silicondust's guide.");
    }

    private static string AddressOf(IptvPlaylist playlist) =>
        XtreamAccount.NormalizeServerUrl(playlist.ServerUrl)
            ?? throw new IptvSourceException("The HDHomeRun needs its address, such as 192.168.1.50.");

    private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AnswerTimeout);
        try
        {
            using var response = await client.GetAsync(url, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new IptvSourceException($"The HDHomeRun answered HTTP {(int)response.StatusCode}.");
            }
            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (HttpRequestException ex)
        {
            throw new IptvSourceException($"The HDHomeRun could not be reached: {ex.Message}", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IptvSourceException("The HDHomeRun did not answer in time.", ex);
        }
    }
}
