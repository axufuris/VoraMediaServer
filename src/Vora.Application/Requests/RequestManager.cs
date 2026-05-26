using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Vora.Application.Media;
using Vora.Application.Requests.Dtos;
using Vora.Application.Requests.ViewModels;
using Vora.Application.Users;
using Vora.Domain.Entities.Requests;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Requests;

public interface IRequestManager
{
    Task ProcessWatchlistAdditionAsync(string externalId, string providerId, string title, string type, string posterUrl, Guid profileId, DateTime? expectedReleaseDate);

    Task<bool> ApproveRequestAsync(Guid requestId, Guid? specificServerId = null, int? overrideProfileId = null, CancellationToken cancellationToken = default);
    Task<List<RequestServerVM>> GetAllServersAsync();
    Task<RequestServer> AddServerAsync(SaveRequestServerDto dto);
    Task UpdateServerAsync(Guid id, SaveRequestServerDto dto);
    Task DeleteServerAsync(Guid id);
    Task<List<MediaRequestVM>> GetAllRequestsAsync();
    Task<IEnumerable<ProviderOptionDto>> GetProviderOptionsAsync(string providerId, string optionType, string host, int port, bool useSsl, string urlBase, string apiKey, CancellationToken cancellationToken = default);
    Task DeleteRequestAsync(Guid id);
    Task ResolveRequestAsync(string externalId, string type, Guid? mediaItemId = null);
    Task<int?> GetRequestStatusAsync(string externalId, string type);
}

public class RequestManager : IRequestManager
{
    private readonly IRequestRepository _requestRepo;
    private readonly IMediaRepository _mediaRepo;
    private readonly IUserRepository _userRepo;
    private readonly IRequestNotificationService _notificationService;
    private readonly IServiceProvider _serviceProvider;

    public RequestManager(
        IRequestRepository requestRepo,
        IMediaRepository mediaRepo,
        IUserRepository userRepo,
        IRequestNotificationService notificationService,
        IServiceProvider serviceProvider)
    {
        _requestRepo = requestRepo;
        _mediaRepo = mediaRepo;
        _userRepo = userRepo;
        _notificationService = notificationService;
        _serviceProvider = serviceProvider;
    }

    public async Task ProcessWatchlistAdditionAsync(string externalId, string providerId, string title, string type, string posterUrl, Guid profileId, DateTime? expectedReleaseDate)
    {
        var alreadyInLibrary = await _mediaRepo.MediaExistsByExternalIdAsync(externalId, type);
        if (alreadyInLibrary) return;

        var profile = await _userRepo.GetProfileByIdAsync(profileId);
        if (profile == null) return;

        var user = await _userRepo.GetUserByIdAsync(profile.UserId);
        if (user == null || !user.CanRequestMedia) return;

        var activeServers = await _requestRepo.GetAllServersAsync();
        var hasValidServer = activeServers.Any(s =>
            s.MediaType.Equals(type, StringComparison.OrdinalIgnoreCase) &&
            s.IsEnabled);

        if (!hasValidServer)
        {
            return;
        }

        var existingRequest = await _requestRepo.GetRequestAsync(externalId, type);

        if (existingRequest != null)
        {
            if (!existingRequest.Requesters.Any(r => r.ProfileId == profileId))
            {
                existingRequest.Requesters.Add(new MediaRequestUser { ProfileId = profileId });
                await _requestRepo.UpdateRequestAsync(existingRequest);
            }
            return;
        }

        var newRequest = new MediaRequest
        {
            ExternalId = externalId,
            ProviderId = providerId,
            Title = title,
            Type = type,
            PosterUrl = posterUrl,
            Status = RequestStatus.Pending,
            ExpectedReleaseDate = expectedReleaseDate
        };
        newRequest.Requesters.Add(new MediaRequestUser { ProfileId = profileId });

        await _requestRepo.AddRequestAsync(newRequest);

        if (user.AutoApproveRequests)
        {
            await ApproveRequestAsync(newRequest.Id);
        }
    }

    public async Task<bool> ApproveRequestAsync(Guid requestId, Guid? specificServerId = null, int? overrideProfileId = null, CancellationToken cancellationToken = default)
    {
        var request = await _requestRepo.GetRequestByIdAsync(requestId);
        if (request == null || request.Status == RequestStatus.Available) return false;

        var serverToUse = await _requestRepo.GetServerAsync(specificServerId, request.Type);
        if (serverToUse == null || !serverToUse.IsEnabled) return false;

        var allPlugins = _serviceProvider.GetServices<IVoraPlugin>();
        var plugin = allPlugins.OfType<IRequestProvider>().FirstOrDefault(p => p.Id == serverToUse.ProviderId);

        if (plugin == null) throw new InvalidOperationException($"Request plugin '{serverToUse.ProviderId}' is not loaded in the system.");
        if (!plugin.SupportedMediaTypes.Contains(request.Type)) return false;

        var settingsJson = serverToUse.ProviderSettingsJson;
        if (overrideProfileId.HasValue && !string.IsNullOrWhiteSpace(settingsJson))
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(settingsJson);
                if (dict != null)
                {
                    dict["qualityProfileId"] = overrideProfileId.Value;
                    settingsJson = JsonSerializer.Serialize(dict);
                }
            }
            catch { }
        }

        var success = await plugin.SubmitRequestAsync(
            request.ExternalId,
            request.Title,
            serverToUse.Hostname,
            serverToUse.Port,
            serverToUse.UseSsl,
            serverToUse.UrlBase,
            serverToUse.ApiKey,
            settingsJson,
            cancellationToken);

        if (success)
        {
            request.Status = RequestStatus.Processing;
            request.AssignedServerId = serverToUse.Id;
            request.UpdatedAt = DateTime.UtcNow;
            await _requestRepo.UpdateRequestAsync(request);
        }

        return success;
    }

    public async Task<List<RequestServerVM>> GetAllServersAsync() => await _requestRepo.GetAllServersAsync();

    public async Task<RequestServer> AddServerAsync(SaveRequestServerDto dto)
    {
        var server = new RequestServer
        {
            Name = dto.Name,
            ProviderId = dto.ProviderId,
            MediaType = dto.MediaType,
            Hostname = dto.Hostname,
            Port = dto.Port,
            UseSsl = dto.UseSsl,
            ApiKey = dto.ApiKey,
            UrlBase = dto.UrlBase,
            IsDefault = dto.IsDefault,
            Is4K = dto.Is4K,
            ProvidesReleaseCalendar = dto.ProvidesReleaseCalendar,
            ProviderSettingsJson = dto.ProviderSettingsJson,
            IsEnabled = dto.IsEnabled
        };

        await _requestRepo.AddServerAsync(server);
        return server;
    }

    public async Task UpdateServerAsync(Guid id, SaveRequestServerDto dto)
    {
        var server = new RequestServer
        {
            Id = id,
            Name = dto.Name,
            ProviderId = dto.ProviderId,
            MediaType = dto.MediaType,
            Hostname = dto.Hostname,
            Port = dto.Port,
            UseSsl = dto.UseSsl,
            ApiKey = dto.ApiKey,
            UrlBase = dto.UrlBase,
            IsDefault = dto.IsDefault,
            Is4K = dto.Is4K,
            ProvidesReleaseCalendar = dto.ProvidesReleaseCalendar,
            ProviderSettingsJson = dto.ProviderSettingsJson,
            IsEnabled = dto.IsEnabled
        };

        await _requestRepo.UpdateServerAsync(server);
    }

    public async Task DeleteServerAsync(Guid id) => await _requestRepo.DeleteServerAsync(id);

    public async Task<List<MediaRequestVM>> GetAllRequestsAsync() => await _requestRepo.GetAllRequestsAsync();

    public async Task<IEnumerable<ProviderOptionDto>> GetProviderOptionsAsync(string providerId, string optionType, string host, int port, bool useSsl, string urlBase, string apiKey, CancellationToken cancellationToken = default)
    {
        var allPlugins = _serviceProvider.GetServices<IVoraPlugin>();
        var plugin = allPlugins.OfType<IRequestProvider>().FirstOrDefault(p => p.Id == providerId);

        if (plugin == null)
        {
            throw new InvalidOperationException($"Request plugin '{providerId}' is not loaded in the system.");
        }

        if (optionType.Equals("qualityProfiles", StringComparison.OrdinalIgnoreCase))
        {
            return await plugin.GetQualityProfilesAsync(host, port, useSsl, urlBase, apiKey, cancellationToken);
        }
        else if (optionType.Equals("rootFolders", StringComparison.OrdinalIgnoreCase))
        {
            return await plugin.GetRootFoldersAsync(host, port, useSsl, urlBase, apiKey, cancellationToken);
        }

        return new List<ProviderOptionDto>();
    }

    public async Task DeleteRequestAsync(Guid id) => await _requestRepo.DeleteRequestAsync(id);

    public async Task ResolveRequestAsync(string externalId, string type, Guid? mediaItemId = null)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return;

        var request = await _requestRepo.GetRequestAsync(externalId, type);

        if (request != null && request.Status == RequestStatus.Processing)
        {
            request.Status = RequestStatus.Available;
            request.UpdatedAt = DateTime.UtcNow;
            await _requestRepo.UpdateRequestAsync(request);

            await _notificationService.NotifyRequestAvailableAsync(request, mediaItemId);
        }
    }

    public async Task<int?> GetRequestStatusAsync(string externalId, string type)
    {
        var request = await _requestRepo.GetRequestAsync(externalId, type);
        return request != null ? (int)request.Status : null;
    }
}