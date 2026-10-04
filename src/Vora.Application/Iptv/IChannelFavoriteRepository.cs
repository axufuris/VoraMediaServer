using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public record FavoriteChannel(IptvChannel Channel, DateTime AddedAt);

public record ChannelFavoriteKey(Guid PlaylistId, string ExternalChannelId);

public interface IChannelFavoriteRepository
{
    Task<List<FavoriteChannel>> GetFavoriteChannelsAsync(Guid profileId, IptvChannelKind kind);
    Task<List<IptvChannel>> FindChannelsByExternalIdsAsync(IReadOnlyCollection<string> externalChannelIds, IptvChannelKind kind);
    Task<List<IptvChannel>> FindChannelsByIdsAsync(IReadOnlyCollection<Guid> channelIds, IptvChannelKind kind);
    Task<bool> ReplaceFavoritesAsync(Guid profileId, IptvChannelKind kind, IReadOnlyCollection<ChannelFavoriteKey> favorites);
}
