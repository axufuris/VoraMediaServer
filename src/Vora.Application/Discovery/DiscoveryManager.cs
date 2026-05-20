using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Domain.Entities.Discovery;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Discovery;

public interface IDiscoveryManager
{
    Task<List<DiscoveryRowConfig>> GetAdminRowConfigsAsync();
    Task UpdateAdminRowConfigsAsync(List<DiscoveryRowConfig> configs);
    Task<IEnumerable<DiscoveryItemDto>> GetRowItemsAsync(string providerId, string rowId, int page = 1);
    Task<DiscoveryItemDetailsDto?> GetItemDetailsAsync(string providerId, string externalId, string type);
    Task<List<UserWatchlistItem>> GetWatchlistAsync(Guid profileId);
    Task ToggleWatchlistAsync(Guid profileId, string externalId, string providerId, string type, string title, string? posterUrl, DateTime? expectedReleaseDate);
    Task<bool> CheckWatchlistStatusAsync(Guid profileId, string externalId, string providerId);
    Task<DiscoveryActorDto?> GetActorDetailsAsync(string providerId, string externalId);
    Task<IEnumerable<DiscoveryItemDto>> SearchAsync(string query);
    Task<IEnumerable<TheaterDto>> GetShowtimesAsync(string movieTitle, string location, DateTime date, int? maxTheaters);
    Task<bool> IsTheaterAutoLoadEnabledAsync();
}

public class DiscoveryManager(
    IEnumerable<IDiscoveryProvider> plugins,
    IEnumerable<IDiscoveryTheaterProvider> theaterPlugins,
    IDiscoveryRepository repository,
    ILogger<DiscoveryManager> logger) : IDiscoveryManager
{
    public async Task<List<DiscoveryRowConfig>> GetAdminRowConfigsAsync()
    {
        var dbConfigs = await repository.GetRowConfigsAsync();
        var availableRows = new List<DiscoveryRowDefinitionDto>();

        foreach (var plugin in plugins)
        {
            availableRows.AddRange(await plugin.GetAvailableRowsAsync());
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

    public async Task UpdateAdminRowConfigsAsync(List<DiscoveryRowConfig> configs)
    {
        for (var i = 0; i < configs.Count; i++)
        {
            configs[i].OrderIndex = i;
        }

        try
        {
            await repository.UpdateRowConfigsAsync(configs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update discovery row configs.");
            throw;
        }
    }

    public async Task<IEnumerable<DiscoveryItemDto>> GetRowItemsAsync(string providerId, string rowId, int page = 1)
    {
        var plugin = plugins.FirstOrDefault(p => p.Id == providerId);
        if (plugin == null)
        {
            logger.LogWarning("Discovery provider {ProviderId} not registered. Row {RowId} cannot be loaded.", providerId, rowId);
            throw new InvalidOperationException($"Discovery provider \"{providerId}\" is not installed or enabled.");
        }

        try
        {
            return await plugin.GetRowItemsAsync(rowId, page);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discovery provider {ProviderId} failed to load row {RowId} (page {Page}).", providerId, rowId, page);
            throw;
        }
    }

    public async Task<DiscoveryItemDetailsDto?> GetItemDetailsAsync(string providerId, string externalId, string type)
    {
        var plugin = plugins.FirstOrDefault(p => p.Id == providerId);
        if (plugin == null)
        {
            return null;
        }

        try
        {
            return await plugin.GetItemDetailsAsync(externalId, type);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discovery provider {ProviderId} failed to load details for {ExternalId} ({Type}).", providerId, externalId, type);
            return null;
        }
    }

    public Task<List<UserWatchlistItem>> GetWatchlistAsync(Guid profileId) =>
        repository.GetWatchlistAsync(profileId);

    public async Task ToggleWatchlistAsync(Guid profileId, string externalId, string providerId, string type, string title, string? posterUrl, DateTime? expectedReleaseDate)
    {
        try
        {
            if (await repository.IsInWatchlistAsync(profileId, externalId, providerId))
            {
                await repository.RemoveFromWatchlistAsync(profileId, externalId, providerId);
                return;
            }

            await repository.AddToWatchlistAsync(new UserWatchlistItem
            {
                ProfileId = profileId,
                ExternalId = externalId,
                ProviderId = providerId,
                Type = type,
                Title = title,
                PosterUrl = posterUrl,
                ExpectedReleaseDate = expectedReleaseDate
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to toggle watchlist entry {ExternalId} from {ProviderId} for profile {ProfileId}.", externalId, providerId, profileId);
            throw;
        }
    }

    public Task<bool> CheckWatchlistStatusAsync(Guid profileId, string externalId, string providerId) =>
        repository.IsInWatchlistAsync(profileId, externalId, providerId);

    public async Task<DiscoveryActorDto?> GetActorDetailsAsync(string providerId, string externalId)
    {
        var plugin = plugins.FirstOrDefault(p => p.Id == providerId);
        if (plugin == null)
        {
            return null;
        }

        try
        {
            return await plugin.GetActorDetailsAsync(externalId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discovery provider {ProviderId} failed to load actor {ExternalId}.", providerId, externalId);
            return null;
        }
    }

    public async Task<IEnumerable<DiscoveryItemDto>> SearchAsync(string query)
    {
        var results = new List<DiscoveryItemDto>();
        foreach (var plugin in plugins)
        {
            try
            {
                results.AddRange(await plugin.SearchAsync(query));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Discovery provider {ProviderId} failed search for '{Query}'.", plugin.Id, query);
            }
        }
        return results;
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
