using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Open.Nat;
using Vora.Application.Settings.ViewModels;

namespace Vora.Application.Settings;

public interface IRemoteAccessManager
{
    Task<RemoteAccessStatusVM> GetStatusAsync();
    Task<RemoteAccessStatusVM> ApplySettingsAsync(UpdateRemoteAccessRequest request);
    Task BootUpnpMappingAsync();
}

public class RemoteAccessManager(
    ISystemSettingsRepository settingsRepo,
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    ILogger<RemoteAccessManager> logger) : IRemoteAccessManager
{
    private const int DefaultPublicPort = 32080;
    private const int DefaultLocalPort = 8080;
    private const string MappingName = "Vora Media Server";
    private const int UpnpDiscoveryTimeoutMs = 5000;
    private const int UpnpCleanupTimeoutMs = 3000;
    private const int PublicIpFallbackTimeoutSeconds = 3;

    public async Task<RemoteAccessStatusVM> GetStatusAsync()
    {
        var settings = await settingsRepo.GetRemoteAccessSettingsAsync();
        return await CheckAndMapPortAsync(
            settings.EnableRemoteAccess,
            settings.ManuallySpecifyPublicPort,
            settings.PublicPort > 0 ? settings.PublicPort : DefaultPublicPort,
            forceUpdate: false);
    }

    public async Task<RemoteAccessStatusVM> ApplySettingsAsync(UpdateRemoteAccessRequest request)
    {
        try
        {
            var settings = await settingsRepo.GetSettingsForUpdateAsync();
            settings.EnableRemoteAccess = request.IsEnabled;
            settings.ManuallySpecifyPublicPort = request.ManuallySpecifyPort;
            settings.PublicPort = request.PublicPort > 0 ? request.PublicPort : DefaultPublicPort;
            await settingsRepo.SaveChangesAsync();

            return await CheckAndMapPortAsync(settings.EnableRemoteAccess, settings.ManuallySpecifyPublicPort, settings.PublicPort, forceUpdate: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply remote-access settings.");
            throw;
        }
    }

    public async Task BootUpnpMappingAsync()
    {
        var settings = await settingsRepo.GetRemoteAccessSettingsAsync();
        if (!settings.EnableRemoteAccess)
        {
            return;
        }

        await CheckAndMapPortAsync(
            isEnabled: true,
            settings.ManuallySpecifyPublicPort,
            settings.PublicPort > 0 ? settings.PublicPort : DefaultPublicPort,
            forceUpdate: true);
    }

    private async Task<RemoteAccessStatusVM> CheckAndMapPortAsync(bool isEnabled, bool manualPort, int publicPort, bool forceUpdate)
    {
        var status = new RemoteAccessStatusVM
        {
            IsEnabled = isEnabled,
            ManuallySpecifyPort = manualPort,
            PublicPort = publicPort,
            LocalIp = GetLocalIpAddress(),
            LocalPort = config.GetValue("InternalPort", DefaultLocalPort)
        };

        if (!isEnabled)
        {
            await RemoveExistingMappingsAsync(status.LocalPort);
            return status;
        }

        try
        {
            var device = await DiscoverNatDeviceAsync(UpnpDiscoveryTimeoutMs);
            status.UpnpSupported = true;
            status.PublicIp = (await device.GetExternalIPAsync()).ToString();

            if (forceUpdate)
            {
                await RemoveExistingMappingsAsync(status.LocalPort, device);
                await device.CreatePortMapAsync(new Mapping(Protocol.Tcp, status.LocalPort, status.PublicPort, MappingName));
            }
        }
        catch (NatDeviceNotFoundException)
        {
            status.UpnpSupported = false;
            status.ErrorMessage = "Your router does not support UPnP, or it is disabled. You will need to manually configure port forwarding in your router's admin panel.";
            status.PublicIp = await GetPublicIpFallbackAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UPnP port mapping failed.");
            status.UpnpSupported = false;
            status.ErrorMessage = $"Failed to configure router: {ex.Message}";
            status.PublicIp = await GetPublicIpFallbackAsync();
        }

        return status;
    }

    private async Task RemoveExistingMappingsAsync(int localPort, NatDevice? specificDevice = null)
    {
        try
        {
            var device = specificDevice ?? await DiscoverNatDeviceAsync(UpnpCleanupTimeoutMs);
            var mappings = await device.GetAllMappingsAsync();
            foreach (var mapping in mappings)
            {
                if (mapping.Description == MappingName || mapping.PrivatePort == localPort)
                {
                    await device.DeletePortMapAsync(mapping);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not clean up existing UPnP mappings (likely no router/UPnP available).");
        }
    }

    private static async Task<NatDevice> DiscoverNatDeviceAsync(int timeoutMs)
    {
        var discoverer = new NatDiscoverer();
        using var cts = new CancellationTokenSource(timeoutMs);
        return await discoverer.DiscoverDeviceAsync(PortMapper.Upnp | PortMapper.Pmp, cts);
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private async Task<string> GetPublicIpFallbackAsync()
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(PublicIpFallbackTimeoutSeconds);
            return (await client.GetStringAsync("https://api.ipify.org")).Trim();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Public IP fallback lookup failed.");
            return "Unknown";
        }
    }
}
