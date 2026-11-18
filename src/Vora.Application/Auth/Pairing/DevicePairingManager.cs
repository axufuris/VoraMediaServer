using Microsoft.Extensions.Logging;
using Vora.Application.Auth.External;
using Vora.Application.Devices;
using Vora.Application.Logging;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Auth.Pairing;

public interface IDevicePairingManager
{
    Task<DevicePairingStartResult> StartAsync(DevicePairingDevice device, string requestOrigin);
    Task<DevicePairingPollVM> PollAsync(string? deviceCode);
    Task<DevicePairingLookupResult> LookupAsync(string? userCode, Guid accountId, string? approverIp);
    Task<DevicePairingDecideOutcome> DecideAsync(string? userCode, Guid accountId, bool approve);
}

public sealed class DevicePairingManager(
    IDevicePairingStore store,
    IAuthManager authManager,
    IUserRepository users,
    ISystemSettingsRepository settingsRepo,
    IAuthProviderRepository providers,
    IDeviceRepository devices,
    IClientLocationResolver locations,
    ILogger<DevicePairingManager> logger) : IDevicePairingManager
{
    public async Task<DevicePairingStartResult> StartAsync(DevicePairingDevice device, string requestOrigin)
    {
        if (await IsBlockedAsync(device.DeviceId))
        {
            return new DevicePairingStartResult(DevicePairingStartOutcome.DeviceBlocked, null);
        }

        var issued = store.Create(device);
        var settings = await settingsRepo.GetSettingsAsync();
        var baseUrl = AuthRedirectUris.BaseUrl(settings?.PublicBaseUrl, settings?.EmailPublicBaseUrl, requestOrigin);

        return new DevicePairingStartResult(DevicePairingStartOutcome.Started, new DevicePairingStartVM
        {
            DeviceCode = issued.DeviceCode,
            UserCode = DevicePairingCode.Format(issued.UserCode),
            VerificationUrl = $"{baseUrl}/link",
            VerificationUrlComplete = $"{baseUrl}/link?code={issued.UserCode}",
            ExpiresInSeconds = (int)DevicePairingStore.Lifetime.TotalSeconds,
            IntervalSeconds = issued.IntervalSeconds
        });
    }

    public async Task<DevicePairingPollVM> PollAsync(string? deviceCode)
    {
        var polled = store.Poll(deviceCode ?? string.Empty);
        switch (polled.Status)
        {
            case DevicePairingPollStatus.Pending:
                return new DevicePairingPollVM { Status = "pending", IntervalSeconds = polled.IntervalSeconds };
            case DevicePairingPollStatus.SlowDown:
                return new DevicePairingPollVM { Status = "slow_down", IntervalSeconds = polled.IntervalSeconds };
            case DevicePairingPollStatus.Expired:
                return new DevicePairingPollVM { Status = "expired" };
            case DevicePairingPollStatus.Denied:
                return new DevicePairingPollVM { Status = "denied" };
        }

        return await DeliverAsync(polled);
    }

    public async Task<DevicePairingLookupResult> LookupAsync(string? userCode, Guid accountId, string? approverIp)
    {
        if (store.IsLockedOut(accountId))
        {
            return new DevicePairingLookupResult(DevicePairingLookupOutcome.LockedOut, null);
        }

        var normalized = DevicePairingCode.Normalize(userCode);
        var snapshot = normalized is null ? null : store.FindPending(normalized);
        if (snapshot is null)
        {
            store.RecordWrongCode(accountId);
            return new DevicePairingLookupResult(DevicePairingLookupOutcome.NotFound, null);
        }

        var device = snapshot.Device;
        var requestIp = device.IpAddress;
        var location = string.IsNullOrWhiteSpace(requestIp) ? ClientAddress.UnknownLocation : await locations.ResolveAsync(requestIp, ClientAddress.UnknownLocation);
        var sameNetwork = !string.IsNullOrWhiteSpace(requestIp)
            && !string.IsNullOrWhiteSpace(approverIp)
            && (string.Equals(requestIp, approverIp, StringComparison.OrdinalIgnoreCase) || (ClientAddress.IsLocal(requestIp) && ClientAddress.IsLocal(approverIp)));

        return new DevicePairingLookupResult(DevicePairingLookupOutcome.Found, new DevicePairingRequestVM
        {
            UserCode = DevicePairingCode.Format(snapshot.UserCode),
            ClientName = device.ClientName ?? DeviceIdentity.UnknownClient,
            DeviceName = device.DeviceName ?? DeviceIdentity.UnknownDevice,
            DeviceType = device.DeviceType ?? DeviceIdentity.UnknownType,
            OperatingSystem = device.OperatingSystem ?? DeviceIdentity.UnknownOperatingSystem,
            Location = location,
            SameNetwork = sameNetwork,
            RequestedAt = snapshot.CreatedAt,
            ExpiresAt = snapshot.ExpiresAt
        });
    }

    public async Task<DevicePairingDecideOutcome> DecideAsync(string? userCode, Guid accountId, bool approve)
    {
        if (store.IsLockedOut(accountId))
        {
            return DevicePairingDecideOutcome.LockedOut;
        }

        var normalized = DevicePairingCode.Normalize(userCode);
        if (normalized is null)
        {
            store.RecordWrongCode(accountId);
            return DevicePairingDecideOutcome.NotFound;
        }

        var user = await users.GetUserByIdAsync(accountId);
        if (user is null)
        {
            return DevicePairingDecideOutcome.AccountNotAllowed;
        }

        if (approve)
        {
            if (!(await SessionAsync(user)).Allowed)
            {
                return DevicePairingDecideOutcome.AccountNotAllowed;
            }

            var pending = store.FindPending(normalized);
            if (pending is not null && await IsBlockedAsync(pending.Device.DeviceId))
            {
                return DevicePairingDecideOutcome.DeviceBlocked;
            }
        }

        var decision = store.Decide(normalized, accountId, approve);
        switch (decision)
        {
            case DevicePairingDecision.NotFound:
                store.RecordWrongCode(accountId);
                return DevicePairingDecideOutcome.NotFound;
            case DevicePairingDecision.AlreadyDecided:
                return DevicePairingDecideOutcome.AlreadyDecided;
        }

        store.ClearWrongCodes(accountId);
        logger.LogInformation("User {UserId} {Decision} a device sign-in", user.Id, approve ? "approved" : "denied");
        return approve ? DevicePairingDecideOutcome.Approved : DevicePairingDecideOutcome.Denied;
    }

    private async Task<DevicePairingPollVM> DeliverAsync(DevicePairingPolled polled)
    {
        var denied = new DevicePairingPollVM { Status = "denied" };
        var deviceId = polled.Device?.DeviceId;
        if (polled.ApprovedBy is not { } accountId || await IsBlockedAsync(deviceId))
        {
            return denied;
        }

        var user = await users.GetUserByIdAsync(accountId);
        if (user is null)
        {
            return denied;
        }

        var session = await SessionAsync(user);
        if (!session.Allowed)
        {
            return denied;
        }

        var auth = await authManager.SignInExternalAsync(accountId);
        if (auth is null)
        {
            return denied;
        }

        auth.RefreshToken = await authManager.IssueRefreshTokenAsync(accountId, deviceId, session.Method);
        logger.LogInformation(
            "Signed in {ClientName} on {DeviceName} for user {UserId} through device pairing",
            LogValue.SingleLine(polled.Device?.ClientName),
            LogValue.SingleLine(polled.Device?.DeviceName),
            user.Id);
        return new DevicePairingPollVM { Status = "approved", Auth = auth };
    }

    private async Task<(bool Allowed, AuthSignInMethod Method)> SessionAsync(User user)
    {
        var settings = await settingsRepo.GetSettingsAsync();
        var passwordWorks = user.IsAdmin || (settings?.PasswordSignInEnabled ?? true);
        var identities = await providers.GetIdentitiesForUserAsync(user.Id);
        var providerWorks = identities.Any(i => i.Provider is { Enabled: true });
        return (passwordWorks || providerWorks, providerWorks ? AuthSignInMethod.External : AuthSignInMethod.Password);
    }

    private async Task<bool> IsBlockedAsync(string? deviceId) =>
        !string.IsNullOrWhiteSpace(deviceId) && await devices.IsDeviceIdBlockedAsync(deviceId);
}
