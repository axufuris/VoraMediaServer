using System.Text.Json;
using Vora.Api.Endpoints;

namespace Vora.Api.Tests;

// The UI-sound toggles ride in the existing PlaybackPrefs blob rather than
// getting their own column, so their whole contract is what System.Text.Json
// does with a stored blob that predates them. These pin that: an absent field
// must leave the property at its initializer, or every existing profile would
// silently have its sounds switched off on first read.
public class ClientPlaybackSettingsSoundTests
{
    // Mirrors ParsePlaybackSettings in ProfileEndpoints.
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static ClientPlaybackSettingsDto Parse(string json) =>
        JsonSerializer.Deserialize<ClientPlaybackSettingsDto>(json, Options)!;

    [Fact]
    public void A_blob_written_before_the_toggles_existed_reports_sounds_on()
    {
        var legacy = Parse("""{"bitrate":8000,"maxResolution":1080,"maxAudioChannels":6}""");

        legacy.SoundOnClick.Should().BeTrue();
        legacy.SoundOnNavOpen.Should().BeTrue();
        legacy.Bitrate.Should().Be(8000);
    }

    [Fact]
    public void A_fresh_instance_reports_sounds_on()
    {
        // The path taken when the stored blob is null or empty.
        var fresh = new ClientPlaybackSettingsDto();

        fresh.SoundOnClick.Should().BeTrue();
        fresh.SoundOnNavOpen.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Both_toggles_round_trip_through_the_blob(bool click, bool navOpen)
    {
        var json = $$"""{"bitrate":0,"maxResolution":0,"maxAudioChannels":0,"soundOnClick":{{click.ToString().ToLowerInvariant()}},"soundOnNavOpen":{{navOpen.ToString().ToLowerInvariant()}}}""";

        var parsed = Parse(json);

        parsed.SoundOnClick.Should().Be(click);
        parsed.SoundOnNavOpen.Should().Be(navOpen);
    }

    [Fact]
    public void Off_survives_the_read_rather_than_falling_back_to_the_default()
    {
        Parse("""{"soundOnClick":false,"soundOnNavOpen":false}""").SoundOnClick.Should().BeFalse();
        Parse("""{"soundOnClick":false,"soundOnNavOpen":false}""").SoundOnNavOpen.Should().BeFalse();
    }

    [Fact]
    public void One_toggle_present_leaves_the_other_at_its_default()
    {
        var parsed = Parse("""{"soundOnClick":false}""");

        parsed.SoundOnClick.Should().BeFalse();
        parsed.SoundOnNavOpen.Should().BeTrue();
    }

    // The client writes the blob, so it decides the casing; the read is
    // case-insensitive and must accept either.
    [Fact]
    public void Casing_does_not_matter()
    {
        Parse("""{"SoundOnClick":false,"SOUNDONNAVOPEN":false}""").SoundOnClick.Should().BeFalse();
        Parse("""{"SoundOnClick":false,"SOUNDONNAVOPEN":false}""").SoundOnNavOpen.Should().BeFalse();
    }

    [Fact]
    public void The_toggles_serialize_into_the_blob_for_a_client_that_reads_it_back()
    {
        var json = JsonSerializer.Serialize(new ClientPlaybackSettingsDto { SoundOnClick = false });

        json.Should().Contain("SoundOnClick");
        json.Should().Contain("SoundOnNavOpen");
    }
}
