using System.Text.Json;
using System.Text.Json.Nodes;
using Vora.Application.Users.ViewModels;

namespace Vora.Application.Users;

public sealed record DeviceSettings(
    int Bitrate,
    int MaxResolution,
    int SeekStepSeconds,
    int VolumeStepPercent,
    bool SoundOnClick,
    bool SoundOnNavOpen,
    bool SoundOnMove)
{
    public const int LargestBitrateMbps = 1000;
    public const int SmallestResolutionHeight = 144;
    public const int LargestResolutionHeight = 4320;

    public static readonly IReadOnlyList<int> SeekSteps = new[] { 5, 10, 15, 30 };
    public static readonly IReadOnlyList<int> VolumeSteps = new[] { 2, 5, 10 };

    public static readonly DeviceSettings Shipped = new(0, 0, 10, 5, true, true, true);

    public static DeviceSettings From(DeviceSettingsVM settings)
    {
        if (settings.Bitrate < 0 || settings.Bitrate > LargestBitrateMbps)
        {
            throw new ArgumentException($"A bitrate cap is 0 (no cap) to {LargestBitrateMbps} Mbps.", nameof(settings));
        }
        if (settings.MaxResolution != 0 && (settings.MaxResolution < SmallestResolutionHeight || settings.MaxResolution > LargestResolutionHeight))
        {
            throw new ArgumentException($"A resolution cap is 0 (no cap) or a height from {SmallestResolutionHeight} to {LargestResolutionHeight}.", nameof(settings));
        }
        if (!SeekSteps.Contains(settings.SeekStepSeconds))
        {
            throw new ArgumentException($"'{settings.SeekStepSeconds}' is not a skip step.", nameof(settings));
        }
        if (!VolumeSteps.Contains(settings.VolumeStepPercent))
        {
            throw new ArgumentException($"'{settings.VolumeStepPercent}' is not a volume step.", nameof(settings));
        }

        return new DeviceSettings(
            settings.Bitrate,
            settings.MaxResolution,
            settings.SeekStepSeconds,
            settings.VolumeStepPercent,
            settings.SoundOnClick,
            settings.SoundOnNavOpen,
            settings.SoundOnMove);
    }

    public DeviceSettingsVM ToViewModel() => new()
    {
        Bitrate = Bitrate,
        MaxResolution = MaxResolution,
        SeekStepSeconds = SeekStepSeconds,
        VolumeStepPercent = VolumeStepPercent,
        SoundOnClick = SoundOnClick,
        SoundOnNavOpen = SoundOnNavOpen,
        SoundOnMove = SoundOnMove
    };
}

public sealed record EffectiveDevicePlayback(DeviceSettings Settings, int MaxAudioChannels);

public sealed class DevicePlaybackBlob
{
    private const string Bitrate = "bitrate";
    private const string MaxResolution = "maxResolution";
    private const string MaxAudioChannels = "maxAudioChannels";
    private const string SeekStepSeconds = "seekStepSeconds";
    private const string VolumeStepPercent = "volumeStepPercent";
    private const string SoundOnClick = "soundOnClick";
    private const string SoundOnNavOpen = "soundOnNavOpen";
    private const string SoundOnMove = "soundOnMove";

    private readonly JsonObject _json;

    private DevicePlaybackBlob(JsonObject json)
    {
        _json = json;
    }

    public static DevicePlaybackBlob Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new DevicePlaybackBlob(new JsonObject());

        try
        {
            return new DevicePlaybackBlob(JsonNode.Parse(json) as JsonObject ?? new JsonObject());
        }
        catch (JsonException)
        {
            return new DevicePlaybackBlob(new JsonObject());
        }
    }

    public EffectiveDevicePlayback Effective(DeviceSettings defaults) => new(
        new DeviceSettings(
            IntOr(Bitrate, defaults.Bitrate),
            IntOr(MaxResolution, defaults.MaxResolution),
            IntOr(SeekStepSeconds, defaults.SeekStepSeconds),
            IntOr(VolumeStepPercent, defaults.VolumeStepPercent),
            BoolOr(SoundOnClick, defaults.SoundOnClick),
            BoolOr(SoundOnNavOpen, defaults.SoundOnNavOpen),
            BoolOr(SoundOnMove, defaults.SoundOnMove)),
        IntOr(MaxAudioChannels, 0));

    public DevicePlaybackBlob With(DeviceSettings settings)
    {
        var json = (JsonObject)_json.DeepClone();
        Set(json, Bitrate, settings.Bitrate);
        Set(json, MaxResolution, settings.MaxResolution);
        Set(json, SeekStepSeconds, settings.SeekStepSeconds);
        Set(json, VolumeStepPercent, settings.VolumeStepPercent);
        Set(json, SoundOnClick, settings.SoundOnClick);
        Set(json, SoundOnNavOpen, settings.SoundOnNavOpen);
        Set(json, SoundOnMove, settings.SoundOnMove);
        return new DevicePlaybackBlob(json);
    }

    public string ToJson() => _json.ToJsonString();

    private int IntOr(string key, int fallback) =>
        ValueOf(_json, key) is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;

    private bool BoolOr(string key, bool fallback) =>
        ValueOf(_json, key) is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;

    private static JsonNode? ValueOf(JsonObject json, string key) =>
        json.FirstOrDefault(property => string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static void Set(JsonObject json, string key, JsonNode value)
    {
        foreach (var existing in json.Where(property => string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase)).Select(property => property.Key).ToList())
        {
            json.Remove(existing);
        }

        json[key] = value;
    }
}
