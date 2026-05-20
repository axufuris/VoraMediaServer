namespace Vora.Application.Media;

public interface IMediaProvider
{
    string ProviderName { get; }

    Task<bool> ValidateConnectionAsync(string accessToken);

    Task ReportPlaybackProgressAsync(string accessToken, string externalMediaId, TimeSpan currentPosition, bool isFinished);
}
