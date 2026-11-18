using System.Security.Cryptography;
using System.Text;

namespace Vora.Application.Auth.Pairing;

public interface IDevicePairingStore
{
    DevicePairingIssued Create(DevicePairingDevice device);
    DevicePairingSnapshot? FindPending(string userCode);
    DevicePairingDecision Decide(string userCode, Guid accountId, bool approve);
    DevicePairingPolled Poll(string deviceCode);
    bool IsLockedOut(Guid accountId);
    void RecordWrongCode(Guid accountId);
    void ClearWrongCodes(Guid accountId);
}

public sealed class DevicePairingStore(TimeProvider clock) : IDevicePairingStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan WrongCodeWindow = TimeSpan.FromMinutes(10);
    public const int StartIntervalSeconds = 5;
    public const int MaxWrongCodes = 5;

    private const int IntervalStepSeconds = 5;
    private const int MaxIntervalSeconds = 30;
    private const int MaxOutstanding = 2000;
    private static readonly TimeSpan PollTolerance = TimeSpan.FromMilliseconds(500);

    private enum State
    {
        Pending,
        Approved,
        Denied
    }

    private sealed class Record
    {
        public required string DeviceCodeHash { get; init; }
        public required string UserCode { get; init; }
        public required DevicePairingDevice Device { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public State State { get; set; } = State.Pending;
        public Guid? ApprovedBy { get; set; }
        public DateTimeOffset? LastPolledAt { get; set; }
        public int IntervalSeconds { get; set; } = StartIntervalSeconds;
    }

    private sealed class WrongCodes
    {
        public int Count { get; set; }
        public DateTimeOffset WindowStart { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Record> _byDeviceCode = new();
    private readonly Dictionary<string, string> _byUserCode = new();
    private readonly Dictionary<Guid, WrongCodes> _wrongCodes = new();

    public DevicePairingIssued Create(DevicePairingDevice device)
    {
        lock (_gate)
        {
            Purge();

            var userCode = DevicePairingCode.Generate();
            while (_byUserCode.ContainsKey(userCode))
            {
                userCode = DevicePairingCode.Generate();
            }

            var deviceCode = Base64Url(RandomNumberGenerator.GetBytes(32));
            var now = clock.GetUtcNow();
            var record = new Record
            {
                DeviceCodeHash = Hash(deviceCode),
                UserCode = userCode,
                Device = device,
                CreatedAt = now,
                ExpiresAt = now.Add(Lifetime)
            };

            _byDeviceCode[record.DeviceCodeHash] = record;
            _byUserCode[userCode] = record.DeviceCodeHash;
            return new DevicePairingIssued(deviceCode, userCode, record.ExpiresAt, StartIntervalSeconds);
        }
    }

    public DevicePairingSnapshot? FindPending(string userCode)
    {
        lock (_gate)
        {
            var record = Find(userCode);
            return record is { State: State.Pending } ? new DevicePairingSnapshot(record.UserCode, record.Device, record.CreatedAt, record.ExpiresAt) : null;
        }
    }

    public DevicePairingDecision Decide(string userCode, Guid accountId, bool approve)
    {
        lock (_gate)
        {
            var record = Find(userCode);
            if (record is null)
            {
                return DevicePairingDecision.NotFound;
            }

            if (record.State != State.Pending)
            {
                return DevicePairingDecision.AlreadyDecided;
            }

            record.State = approve ? State.Approved : State.Denied;
            record.ApprovedBy = approve ? accountId : null;
            return approve ? DevicePairingDecision.Approved : DevicePairingDecision.Denied;
        }
    }

    public DevicePairingPolled Poll(string deviceCode)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (string.IsNullOrWhiteSpace(deviceCode) || !_byDeviceCode.TryGetValue(Hash(deviceCode), out var record))
            {
                return new DevicePairingPolled(DevicePairingPollStatus.Expired, StartIntervalSeconds, null, null);
            }

            if (record.ExpiresAt <= now)
            {
                Remove(record);
                return new DevicePairingPolled(DevicePairingPollStatus.Expired, StartIntervalSeconds, null, null);
            }

            var tooSoon = record.LastPolledAt is { } last && now - last + PollTolerance < TimeSpan.FromSeconds(record.IntervalSeconds);
            record.LastPolledAt = now;
            if (tooSoon)
            {
                record.IntervalSeconds = Math.Min(record.IntervalSeconds + IntervalStepSeconds, MaxIntervalSeconds);
                return new DevicePairingPolled(DevicePairingPollStatus.SlowDown, record.IntervalSeconds, null, null);
            }

            switch (record.State)
            {
                case State.Approved:
                    Remove(record);
                    return new DevicePairingPolled(DevicePairingPollStatus.Approved, record.IntervalSeconds, record.ApprovedBy, record.Device);
                case State.Denied:
                    Remove(record);
                    return new DevicePairingPolled(DevicePairingPollStatus.Denied, record.IntervalSeconds, null, null);
                default:
                    return new DevicePairingPolled(DevicePairingPollStatus.Pending, record.IntervalSeconds, null, null);
            }
        }
    }

    public bool IsLockedOut(Guid accountId)
    {
        lock (_gate)
        {
            return _wrongCodes.TryGetValue(accountId, out var entry) && entry.LockedUntil is { } until && until > clock.GetUtcNow();
        }
    }

    public void RecordWrongCode(Guid accountId)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (!_wrongCodes.TryGetValue(accountId, out var entry) || now - entry.WindowStart > WrongCodeWindow)
            {
                entry = new WrongCodes { WindowStart = now };
                _wrongCodes[accountId] = entry;
            }

            entry.Count++;
            if (entry.Count >= MaxWrongCodes)
            {
                entry.LockedUntil = now.Add(WrongCodeWindow);
            }
        }
    }

    public void ClearWrongCodes(Guid accountId)
    {
        lock (_gate)
        {
            _wrongCodes.Remove(accountId);
        }
    }

    private Record? Find(string userCode)
    {
        if (!_byUserCode.TryGetValue(userCode, out var hash) || !_byDeviceCode.TryGetValue(hash, out var record))
        {
            return null;
        }

        if (record.ExpiresAt <= clock.GetUtcNow())
        {
            Remove(record);
            return null;
        }

        return record;
    }

    private void Remove(Record record)
    {
        _byDeviceCode.Remove(record.DeviceCodeHash);
        _byUserCode.Remove(record.UserCode);
    }

    private void Purge()
    {
        var now = clock.GetUtcNow();
        foreach (var record in _byDeviceCode.Values.Where(r => r.ExpiresAt <= now).ToList())
        {
            Remove(record);
        }

        foreach (var pair in _wrongCodes.Where(p => now - p.Value.WindowStart > WrongCodeWindow && (p.Value.LockedUntil is null || p.Value.LockedUntil <= now)).ToList())
        {
            _wrongCodes.Remove(pair.Key);
        }

        if (_byDeviceCode.Count >= MaxOutstanding)
        {
            foreach (var record in _byDeviceCode.Values.OrderBy(r => r.ExpiresAt).Take(_byDeviceCode.Count - MaxOutstanding + 1).ToList())
            {
                Remove(record);
            }
        }
    }

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
