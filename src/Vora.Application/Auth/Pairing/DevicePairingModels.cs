using Vora.Application.Auth.Dtos;

namespace Vora.Application.Auth.Pairing;

public sealed record DevicePairingDevice(
    string? DeviceId,
    string? ClientName,
    string? DeviceName,
    string? DeviceType,
    string? OperatingSystem,
    string? IpAddress);

public sealed record DevicePairingIssued(string DeviceCode, string UserCode, DateTimeOffset ExpiresAt, int IntervalSeconds);

public sealed record DevicePairingSnapshot(string UserCode, DevicePairingDevice Device, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public enum DevicePairingDecision
{
    Approved,
    Denied,
    NotFound,
    AlreadyDecided
}

public enum DevicePairingPollStatus
{
    Pending,
    SlowDown,
    Approved,
    Denied,
    Expired
}

public sealed record DevicePairingPolled(DevicePairingPollStatus Status, int IntervalSeconds, Guid? ApprovedBy, DevicePairingDevice? Device);

public enum DevicePairingStartOutcome
{
    Started,
    DeviceBlocked
}

public sealed record DevicePairingStartResult(DevicePairingStartOutcome Outcome, DevicePairingStartVM? Pairing);

public enum DevicePairingLookupOutcome
{
    Found,
    NotFound,
    LockedOut
}

public sealed record DevicePairingLookupResult(DevicePairingLookupOutcome Outcome, DevicePairingRequestVM? Request);

public enum DevicePairingDecideOutcome
{
    Approved,
    Denied,
    NotFound,
    AlreadyDecided,
    LockedOut,
    AccountNotAllowed,
    DeviceBlocked
}

public sealed class DevicePairingStartVM
{
    public required string DeviceCode { get; set; }
    public required string UserCode { get; set; }
    public required string VerificationUrl { get; set; }
    public required string VerificationUrlComplete { get; set; }
    public required int ExpiresInSeconds { get; set; }
    public required int IntervalSeconds { get; set; }
}

public sealed class DevicePairingPollVM
{
    public required string Status { get; set; }
    public int? IntervalSeconds { get; set; }
    public AuthResponseDto? Auth { get; set; }
}

public sealed class DevicePairingRequestVM
{
    public required string UserCode { get; set; }
    public required string ClientName { get; set; }
    public required string DeviceName { get; set; }
    public required string DeviceType { get; set; }
    public required string OperatingSystem { get; set; }
    public required string Location { get; set; }
    public required bool SameNetwork { get; set; }
    public required DateTimeOffset RequestedAt { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
}
