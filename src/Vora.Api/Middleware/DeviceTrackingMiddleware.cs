using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Vora.Api.Extensions;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Middleware;

public class DeviceTrackingMiddleware
{
    public const string GeoLookupHttpClientName = "GeoLookup";

    private const string DeviceIdHeader = "X-Vora-Device-Id";
    private const string ClientNameHeader = "X-Vora-Client";
    private const string DeviceNameHeader = "X-Vora-Device";
    private const string DeviceTypeHeader = "X-Vora-Device-Type";
    private const string OperatingSystemHeader = "X-Vora-OS";
    private const string ForwardedForHeader = "X-Forwarded-For";

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DeviceLocks = new();

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;

    public DeviceTrackingMiddleware(RequestDelegate next, IMemoryCache cache)
    {
        _next = next;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext context, IServiceProvider serviceProvider)
    {
        var deviceId = context.Request.Headers[DeviceIdHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(deviceId))
        {
            await _next(context);
            return;
        }

        if (await IsDeviceBlockedAsync(context, deviceId))
        {
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var accountId = context.User.GetAccountId();
        var profileId = context.User.GetProfileId();

        if (accountId == null)
        {
            await _next(context);
            return;
        }

        var cacheKey = $"DeviceTrack_{deviceId}";
        if (!_cache.TryGetValue(cacheKey, out _))
        {
            var blockedDuringLookup = await TrackDeviceAsync(context, serviceProvider, deviceId, accountId.Value, profileId, cacheKey);
            if (blockedDuringLookup)
            {
                return;
            }
        }

        await _next(context);
    }

    private async Task<bool> IsDeviceBlockedAsync(HttpContext context, string deviceId)
    {
        if (_cache.TryGetValue($"BlockedDevice_{deviceId}", out bool isBlocked) && isBlocked)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("This device has been blocked by the server administrator.");
            return true;
        }
        return false;
    }

    private async Task<bool> TrackDeviceAsync(
        HttpContext context,
        IServiceProvider serviceProvider,
        string deviceId,
        Guid accountId,
        Guid? profileId,
        string cacheKey)
    {
        var deviceLock = DeviceLocks.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));
        await deviceLock.WaitAsync();

        try
        {
            if (_cache.TryGetValue(cacheKey, out _))
            {
                return false;
            }

            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

            var existingDevice = dbContext.ClientDevices.FirstOrDefault(d => d.DeviceId == deviceId);
            var ip = ResolveClientIp(context);

            if (existingDevice?.IsBlocked == true)
            {
                _cache.Set($"BlockedDevice_{deviceId}", true, CacheLifetime);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("This device has been blocked by the server administrator.");
                return true;
            }

            var (device, ipChanged) = UpsertDevice(dbContext, existingDevice, context.Request, deviceId, ip, accountId, profileId);

            if (ipChanged)
            {
                device.Location = await ResolveLocationAsync(httpClientFactory, ip, device.Location);
            }

            await dbContext.SaveChangesAsync();
            _cache.Set(cacheKey, true, CacheLifetime);
            return false;
        }
        finally
        {
            deviceLock.Release();
        }
    }

    private static (ClientDevice Device, bool IpChanged) UpsertDevice(
        VoraDbContext dbContext,
        ClientDevice? existingDevice,
        HttpRequest request,
        string deviceId,
        string ip,
        Guid accountId,
        Guid? profileId)
    {
        if (existingDevice != null)
        {
            var ipChanged = existingDevice.LastIpAddress != ip;

            existingDevice.LastConnectedAt = DateTime.UtcNow;
            existingDevice.LastIpAddress = ip;
            existingDevice.ClientName = request.Headers[ClientNameHeader].FirstOrDefault() ?? existingDevice.ClientName;
            existingDevice.DeviceName = request.Headers[DeviceNameHeader].FirstOrDefault() ?? existingDevice.DeviceName;
            existingDevice.DeviceType = request.Headers[DeviceTypeHeader].FirstOrDefault() ?? existingDevice.DeviceType;
            existingDevice.OperatingSystem = request.Headers[OperatingSystemHeader].FirstOrDefault() ?? existingDevice.OperatingSystem;
            existingDevice.LastUserId = accountId;
            if (profileId.HasValue)
            {
                existingDevice.LastProfileId = profileId.Value;
            }

            return (existingDevice, ipChanged);
        }

        var newDevice = new ClientDevice
        {
            DeviceId = deviceId,
            ClientName = request.Headers[ClientNameHeader].FirstOrDefault() ?? "Unknown Client",
            DeviceName = request.Headers[DeviceNameHeader].FirstOrDefault() ?? "Unknown Device",
            DeviceType = request.Headers[DeviceTypeHeader].FirstOrDefault() ?? "Unknown Type",
            OperatingSystem = request.Headers[OperatingSystemHeader].FirstOrDefault() ?? "Unknown OS",
            LastIpAddress = ip,
            LastConnectedAt = DateTime.UtcNow,
            LastUserId = accountId,
            LastProfileId = profileId
        };
        dbContext.ClientDevices.Add(newDevice);
        return (newDevice, true);
    }

    private static string ResolveClientIp(HttpContext context)
    {
        var ip = context.Request.Headers[ForwardedForHeader].FirstOrDefault()
                 ?? context.Connection.RemoteIpAddress?.ToString()
                 ?? "Unknown IP";

        if (ip.Contains(','))
        {
            ip = ip.Split(',')[0].Trim();
        }

        if (ip.StartsWith("::ffff:"))
        {
            ip = ip[7..];
        }

        return ip;
    }

    private static async Task<string> ResolveLocationAsync(IHttpClientFactory httpClientFactory, string ip, string? fallback)
    {
        if (IsLocalIp(ip))
        {
            return "Local Network";
        }

        try
        {
            var client = httpClientFactory.CreateClient(GeoLookupHttpClientName);
            var response = await client.GetStringAsync($"http://ip-api.com/json/{ip}");
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "success")
            {
                var city = doc.RootElement.GetProperty("city").GetString();
                var region = doc.RootElement.GetProperty("region").GetString();
                var country = doc.RootElement.GetProperty("countryCode").GetString();
                return $"{city}, {region}, {country}";
            }
        }
        catch
        {
        }

        return string.IsNullOrEmpty(fallback) ? "Unknown Location" : fallback;
    }

    private static bool IsLocalIp(string ip) =>
        ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("127.") || ip == "::1";
}
