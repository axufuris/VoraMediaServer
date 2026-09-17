using Microsoft.Extensions.Logging;
using Vora.Application.Discovery.Requests;
using Vora.Application.Discovery.ViewModels;
using Vora.Application.Media;
using Vora.Application.Requests;
using Vora.Plugins.Dtos;
using Vora.Domain.Entities.Discovery;
using Vora.Domain.Entities.Requests;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Discovery;

public interface IDiscoveryManager
{
    Task<List<DiscoveryRowConfig>> GetAdminRowConfigsAsync(CancellationToken cancellationToken = default);
    Task UpdateAdminRowConfigsAsync(List<DiscoveryRowConfigRequest> configs);
    Task<IEnumerable<DiscoveryItemVM>> GetRowItemsAsync(string providerId, string rowId, int page = 1, CancellationToken cancellationToken = default);
    Task<DiscoveryItemDetailsVM?> GetItemDetailsAsync(string providerId, string externalId, string type, CancellationToken cancellationToken = default);
    Task<DiscoveryActorVM?> GetActorDetailsAsync(string providerId, string externalId, CancellationToken cancellationToken = default);
    Task<IEnumerable<DiscoveryItemVM>> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task<IEnumerable<TheaterDto>> GetShowtimesAsync(string movieTitle, string location, DateTime date, int? maxTheaters);
    Task<bool> IsTheaterAutoLoadEnabledAsync();
}

public class DiscoveryManager(
    IEnumerable<IDiscoveryProvider> plugins,
    IEnumerable<IDiscoveryTheaterProvider> theaterPlugins,
    IDiscoveryRepository repository,
    IMediaRepository mediaRepository,
    IRequestRepository requestRepository,
    ILogger<DiscoveryManager> logger) : IDiscoveryManager
{
    public async Task<List<DiscoveryRowConfig>> GetAdminRowConfigsAsync(CancellationToken cancellationToken = default)
    {
        var dbConfigs = await repository.GetRowConfigsAsync();
        var availableRows = new List<DiscoveryRowDefinitionDto>();

        foreach (var plugin in plugins)
        {
            availableRows.AddRange(await plugin.GetAvailableRowsAsync(cancellationToken));
        }

        var result = new List<DiscoveryRowConfig>();
        var order = 0;

        foreach (var dbRow in dbConfigs)
        {
            var matchingPlugin = plugins.FirstOrDefault(p => p.Id == dbRow.ProviderId);
            var matchingPluginRow = availableRows.FirstOrDefault(r => r.Id == dbRow.RowId && r.ProviderId == dbRow.ProviderId);

            if (matchingPluginRow == null)
            {
                continue;
            }

            dbRow.ProviderName = matchingPlugin?.ProviderName ?? matchingPlugin?.Name ?? dbRow.ProviderId;
            result.Add(dbRow);
            availableRows.Remove(matchingPluginRow);
            order = Math.Max(order, dbRow.OrderIndex) + 1;
        }

        foreach (var newRow in availableRows)
        {
            var matchingPlugin = plugins.FirstOrDefault(p => p.Id == newRow.ProviderId);
            result.Add(new DiscoveryRowConfig
            {
                Id = Guid.NewGuid(),
                RowId = newRow.Id,
                ProviderId = newRow.ProviderId,
                Name = newRow.Name,
                ProviderName = matchingPlugin?.ProviderName ?? matchingPlugin?.Name ?? newRow.ProviderId,
                OrderIndex = order++,
                IsEnabled = false
            });
        }

        return result.OrderBy(r => r.OrderIndex).ToList();
    }

    public async Task UpdateAdminRowConfigsAsync(List<DiscoveryRowConfigRequest> configs)
    {
        var entities = new List<DiscoveryRowConfig>(configs.Count);
        for (var i = 0; i < configs.Count; i++)
        {
            var source = configs[i];
            entities.Add(new DiscoveryRowConfig
            {
                Id = Guid.NewGuid(),
                RowId = source.RowId,
                ProviderId = source.ProviderId,
                Name = source.Name,
                OrderIndex = i,
                IsEnabled = source.IsEnabled
            });
        }

        try
        {
            await repository.UpdateRowConfigsAsync(entities);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update discovery row configs.");
            throw;
        }
    }

    public async Task<IEnumerable<DiscoveryItemVM>> GetRowItemsAsync(string providerId, string rowId, int page = 1, CancellationToken cancellationToken = default)
    {
        var dbConfigs = await repository.GetRowConfigsAsync();
        var rowConfig = dbConfigs.FirstOrDefault(c => c.ProviderId == providerId && c.RowId == rowId);
        if (rowConfig == null || !rowConfig.IsEnabled)
        {
            logger.LogInformation("Discovery row {ProviderId}/{RowId} requested but is disabled or no longer configured.", providerId, rowId);
            throw new KeyNotFoundException($"Discovery row \"{rowId}\" is not available.");
        }

        var plugin = plugins.FirstOrDefault(p => p.Id == providerId);
        if (plugin == null)
        {
            logger.LogWarning("Discovery provider {ProviderId} not registered. Row {RowId} cannot be loaded.", providerId, rowId);
            throw new InvalidOperationException($"Discovery provider \"{providerId}\" is not installed or enabled.");
        }

        try
        {
            var items = await plugin.GetRowItemsAsync(rowId, page, cancellationToken);
            return await EnrichWithStatusAsync(items);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discovery provider {ProviderId} failed to load row {RowId} (page {Page}).", providerId, rowId, page);
            throw;
        }
    }

    private async Task<List<DiscoveryItemVM>> EnrichWithStatusAsync(IEnumerable<DiscoveryItemDto> items)
    {
        var list = items.ToList();

        var existing = new HashSet<(string ExternalId, string Type)>();
        var localIdByKey = new Dictionary<(string ExternalId, string Type), Guid>();
        var requestByKey = new Dictionary<(string ExternalId, string Type), MediaRequest>();

        var groups = list
            .Where(i => !string.IsNullOrWhiteSpace(i.ExternalId))
            .GroupBy(i => i.Type);

        foreach (var group in groups)
        {
            var type = group.Key;
            var ids = group.Select(i => i.ExternalId).Distinct().ToList();

            foreach (var pair in await mediaRepository.GetLocalIdsByExternalIdsAsync(ids, type))
            {
                existing.Add((pair.Key, type));
                localIdByKey[(pair.Key, type)] = pair.Value;
            }

            foreach (var pair in await requestRepository.GetRequestsAsync(ids, type))
            {
                requestByKey[(pair.Key, type)] = pair.Value;
            }
        }

        var result = new List<DiscoveryItemVM>(list.Count);
        foreach (var item in list)
        {
            var hasId = !string.IsNullOrWhiteSpace(item.ExternalId);
            var key = (item.ExternalId, item.Type);

            result.Add(new DiscoveryItemVM
            {
                ExternalId = item.ExternalId,
                ProviderId = item.ProviderId,
                Title = item.Title,
                Type = item.Type,
                Year = item.Year,
                ReleaseDate = item.ReleaseDate,
                PosterUrl = item.PosterUrl,
                ContentRating = item.ContentRating,
                InLibrary = hasId && existing.Contains(key),
                MediaItemId = hasId && localIdByKey.TryGetValue(key, out var localId) ? localId : null,
                RequestStatus = hasId && requestByKey.TryGetValue(key, out var request) ? request.Status : null
            });
        }
        return result;
    }

    public async Task<DiscoveryItemDetailsVM?> GetItemDetailsAsync(string providerId, string externalId, string type, CancellationToken cancellationToken = default)
    {
        var plugin = plugins.FirstOrDefault(p => p.Id == providerId);
        if (plugin == null)
        {
            return null;
        }

        try
        {
            var details = await plugin.GetItemDetailsAsync(externalId, type, cancellationToken);
            if (details == null)
            {
                return null;
            }

            var inLibrary = !string.IsNullOrWhiteSpace(details.ExternalId)
                && (await mediaRepository.GetExistingExternalIdsAsync([details.ExternalId], details.Type)).Any();

            return DiscoveryItemDetailsVM.FromDto(details, inLibrary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discovery provider {ProviderId} failed to load details for {ExternalId} ({Type}).", providerId, externalId, type);
            return null;
        }
    }

    public async Task<DiscoveryActorVM?> GetActorDetailsAsync(string providerId, string externalId, CancellationToken cancellationToken = default)
    {
        var plugin = plugins.FirstOrDefault(p => p.Id == providerId);
        if (plugin == null)
        {
            return null;
        }

        try
        {
            var actor = await plugin.GetActorDetailsAsync(externalId, cancellationToken);
            if (actor == null) return null;

            var deduped = actor.Filmography
                .Where(i => !string.IsNullOrWhiteSpace(i.ExternalId))
                .GroupBy(i => (i.ExternalId, i.Type))
                .Select(g => g.First())
                .ToList();

            return new DiscoveryActorVM
            {
                ExternalId = actor.ExternalId,
                ProviderId = actor.ProviderId,
                Name = actor.Name,
                Biography = actor.Biography,
                PlaceOfBirth = actor.PlaceOfBirth,
                Birthday = actor.Birthday,
                Deathday = actor.Deathday,
                ProfileImageUrl = actor.ProfileImageUrl,
                Filmography = await EnrichWithStatusAsync(deduped)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discovery provider {ProviderId} failed to load actor {ExternalId}.", providerId, externalId);
            return null;
        }
    }

    public async Task<IEnumerable<DiscoveryItemVM>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<DiscoveryItemDto>();
        foreach (var plugin in plugins)
        {
            try
            {
                results.AddRange(await plugin.SearchAsync(query, cancellationToken));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Discovery provider {ProviderId} failed search for '{Query}'.", plugin.Id, query);
            }
        }
        return await EnrichWithStatusAsync(results);
    }

    public async Task<IEnumerable<TheaterDto>> GetShowtimesAsync(string movieTitle, string location, DateTime date, int? maxTheaters)
    {
        var plugin = theaterPlugins.FirstOrDefault();
        if (plugin == null)
        {
            return new List<TheaterDto>();
        }

        try
        {
            return await plugin.GetShowtimesAsync(movieTitle, location, date, maxTheaters);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Theater provider failed showtimes lookup for '{MovieTitle}' near '{Location}'.", movieTitle, location);
            return new List<TheaterDto>();
        }
    }

    public async Task<bool> IsTheaterAutoLoadEnabledAsync()
    {
        var plugin = theaterPlugins.FirstOrDefault();
        return plugin != null && await plugin.IsAutoLoadEnabledAsync();
    }
}
