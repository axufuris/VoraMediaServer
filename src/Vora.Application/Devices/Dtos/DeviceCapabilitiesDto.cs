using System.Text.Json.Serialization;

namespace Vora.Application.Devices.Dtos;

public class DeviceCapabilitiesDto
{
    [JsonPropertyName("videoCodecs")]
    public List<string> VideoCodecs { get; set; } = new();

    [JsonPropertyName("audioCodecs")]
    public List<string> AudioCodecs { get; set; } = new();

    [JsonPropertyName("containers")]
    public List<string> Containers { get; set; } = new();

    [JsonPropertyName("maxAudioChannels")]
    public int MaxAudioChannels { get; set; } = 2;

    [JsonPropertyName("supportedHdrFormats")]
    public List<string>? SupportedHdrFormats { get; set; }

    [JsonPropertyName("maxVideoBitDepth")]
    public int MaxVideoBitDepth { get; set; }
}
