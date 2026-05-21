using System.Collections.Concurrent;

namespace Vora.Application.Logging;

public sealed class LogLevelOverrideProvider
{
    private readonly ConcurrentDictionary<string, VoraLogLevel> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private VoraLogLevel _defaultLevel = VoraLogLevel.Information;

    public VoraLogLevel DefaultLevel
    {
        get => _defaultLevel;
        set => _defaultLevel = value;
    }

    public IReadOnlyDictionary<string, VoraLogLevel> Overrides => _overrides;

    public void SetOverride(string category, VoraLogLevel level)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        _overrides[category] = level;
    }

    public bool RemoveOverride(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return false;
        return _overrides.TryRemove(category, out _);
    }

    public VoraLogLevel ResolveEffectiveLevel(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return _defaultLevel;

        if (_overrides.TryGetValue(category, out var exact))
        {
            return exact;
        }

        VoraLogLevel? best = null;
        var bestPrefixLength = -1;
        foreach (var kvp in _overrides)
        {
            if (category.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase)
                && kvp.Key.Length > bestPrefixLength)
            {
                best = kvp.Value;
                bestPrefixLength = kvp.Key.Length;
            }
        }

        return best ?? _defaultLevel;
    }

    public bool IsEnabled(string category, VoraLogLevel level)
    {
        return level >= ResolveEffectiveLevel(category);
    }
}
