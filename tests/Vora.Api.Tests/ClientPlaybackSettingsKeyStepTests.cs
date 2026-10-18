using System.Text.Json;
using Vora.Api.Endpoints;

namespace Vora.Api.Tests;

public class ClientPlaybackSettingsKeyStepTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static ClientPlaybackSettingsDto Parse(string json) =>
        JsonSerializer.Deserialize<ClientPlaybackSettingsDto>(json, Options) ?? new ClientPlaybackSettingsDto();

    [Fact]
    public void The_steps_the_web_saves_come_back_from_the_blob()
    {
        var parsed = Parse("""{"bitrate":8000,"maxResolution":1080,"maxAudioChannels":6,"seekStepSeconds":30,"volumeStepPercent":10}""");

        (parsed.SeekStepSeconds, parsed.VolumeStepPercent).Should().Be((30, 10));
        parsed.Bitrate.Should().Be(8000);
    }

    [Fact]
    public void A_blob_written_before_the_steps_existed_gets_the_player_defaults()
    {
        var legacy = Parse("""{"bitrate":8000,"maxResolution":1080,"maxAudioChannels":6}""");

        (legacy.SeekStepSeconds, legacy.VolumeStepPercent).Should().Be((10, 5));
        (new ClientPlaybackSettingsDto().SeekStepSeconds, new ClientPlaybackSettingsDto().VolumeStepPercent).Should().Be((10, 5));
    }

    [Fact]
    public void A_blob_from_a_tv_app_keeps_its_steps_beside_its_sound_toggles()
    {
        var parsed = Parse("""{"SeekStepSeconds":15,"VolumeStepPercent":2,"soundOnClick":false}""");

        (parsed.SeekStepSeconds, parsed.VolumeStepPercent, parsed.SoundOnClick).Should().Be((15, 2, false));
    }
}
