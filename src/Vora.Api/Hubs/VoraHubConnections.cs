using Microsoft.AspNetCore.Http.Connections;

namespace Vora.Api.Hubs;

public static class VoraHubConnections
{
    public static readonly TimeSpan LongPollTimeout = TimeSpan.FromSeconds(45);

    public static void Configure(HttpConnectionDispatcherOptions options)
    {
        options.LongPolling.PollTimeout = LongPollTimeout;
    }
}
