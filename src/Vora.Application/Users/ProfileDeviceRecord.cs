namespace Vora.Application.Users;

public sealed record ProfileDeviceRecord(
    string DeviceId,
    string? PlaybackPrefs,
    string DeviceName,
    string ClientName,
    string DeviceType,
    string OperatingSystem,
    DateTime? LastConnectedAt);
