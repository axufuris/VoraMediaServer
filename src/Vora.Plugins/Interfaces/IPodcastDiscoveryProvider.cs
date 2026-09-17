using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IPodcastDiscoveryProvider : IVoraPlugin
{
    Task<IReadOnlyList<DiscoveredPodcast>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}
