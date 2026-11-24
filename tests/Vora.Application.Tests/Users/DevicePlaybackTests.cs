using System.Text.Json.Nodes;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;

namespace Vora.Application.Tests.Users;

public class DevicePlaybackTests
{
    private static readonly DeviceSettings Defaults = new(20, 1080, 15, 10, false, false, true);

    private static DeviceSettingsVM Valid() => DeviceSettings.Shipped.ToViewModel();

    [Fact]
    public void A_device_that_saved_nothing_gets_every_default()
    {
        var effective = DevicePlaybackBlob.Parse(null).Effective(Defaults);

        effective.Settings.Should().Be(Defaults);
        effective.MaxAudioChannels.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("10")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void A_blob_that_is_not_an_object_reads_as_nothing_saved(string stored)
    {
        DevicePlaybackBlob.Parse(stored).Effective(Defaults).Settings.Should().Be(Defaults);
    }

    [Fact]
    public void A_key_the_blob_lacks_falls_to_the_default_and_one_it_has_wins_even_at_zero()
    {
        var effective = DevicePlaybackBlob.Parse("""{"bitrate":0,"maxResolution":720,"soundOnClick":true}""").Effective(Defaults);

        effective.Settings.Bitrate.Should().Be(0);
        effective.Settings.MaxResolution.Should().Be(720);
        effective.Settings.SoundOnClick.Should().BeTrue();
        effective.Settings.SeekStepSeconds.Should().Be(15);
        effective.Settings.VolumeStepPercent.Should().Be(10);
        effective.Settings.SoundOnNavOpen.Should().BeFalse();
        effective.Settings.SoundOnMove.Should().BeTrue();
    }

    [Fact]
    public void Keys_in_any_case_are_read_as_the_tv_apps_write_them()
    {
        var effective = DevicePlaybackBlob.Parse("""{"SeekStepSeconds":30,"VolumeStepPercent":2,"SoundOnClick":true,"MaxAudioChannels":6}""").Effective(Defaults);

        (effective.Settings.SeekStepSeconds, effective.Settings.VolumeStepPercent, effective.Settings.SoundOnClick, effective.MaxAudioChannels)
            .Should().Be((30, 2, true, 6));
    }

    [Fact]
    public void A_value_of_the_wrong_kind_is_the_same_as_none()
    {
        var effective = DevicePlaybackBlob.Parse("""{"bitrate":"fast","seekStepSeconds":10.5,"soundOnMove":"yes","volumeStepPercent":null}""").Effective(Defaults);

        effective.Settings.Should().Be(Defaults);
    }

    [Fact]
    public void An_admin_write_sets_the_seven_settings_and_keeps_the_audio_layout_pick_and_any_other_key()
    {
        var written = DevicePlaybackBlob.Parse("""{"bitrate":4,"maxAudioChannels":6,"somethingElse":"kept"}""")
            .With(new DeviceSettings(40, 2160, 5, 2, false, true, false));

        var json = JsonNode.Parse(written.ToJson())!.AsObject();
        json["bitrate"]!.GetValue<int>().Should().Be(40);
        json["maxResolution"]!.GetValue<int>().Should().Be(2160);
        json["seekStepSeconds"]!.GetValue<int>().Should().Be(5);
        json["volumeStepPercent"]!.GetValue<int>().Should().Be(2);
        json["soundOnClick"]!.GetValue<bool>().Should().BeFalse();
        json["soundOnNavOpen"]!.GetValue<bool>().Should().BeTrue();
        json["soundOnMove"]!.GetValue<bool>().Should().BeFalse();
        json["maxAudioChannels"]!.GetValue<int>().Should().Be(6);
        json["somethingElse"]!.GetValue<string>().Should().Be("kept");
    }

    [Fact]
    public void An_admin_write_replaces_a_key_written_in_another_case_instead_of_adding_a_second()
    {
        var written = DevicePlaybackBlob.Parse("""{"SeekStepSeconds":30,"SoundOnClick":true}""")
            .With(DeviceSettings.Shipped with { SeekStepSeconds = 5, SoundOnClick = false });

        var json = JsonNode.Parse(written.ToJson())!.AsObject();
        json.Select(property => property.Key.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
        DevicePlaybackBlob.Parse(written.ToJson()).Effective(Defaults).Settings.Should().Match<DeviceSettings>(settings =>
            settings.SeekStepSeconds == 5 && !settings.SoundOnClick);
    }

    [Fact]
    public void A_write_leaves_the_blob_it_started_from_alone()
    {
        var original = DevicePlaybackBlob.Parse("""{"bitrate":4}""");

        original.With(DeviceSettings.Shipped with { Bitrate = 40 });

        original.ToJson().Should().Be("""{"bitrate":4}""");
    }

    [Fact]
    public void A_valid_set_of_settings_round_trips_through_its_view_model()
    {
        var settings = new DeviceSettings(40, 2160, 30, 2, false, true, false);

        DeviceSettings.From(settings.ToViewModel()).Should().Be(settings);
    }

    [Theory]
    [InlineData(-1, 0, 10, 5)]
    [InlineData(1001, 0, 10, 5)]
    [InlineData(0, 100, 10, 5)]
    [InlineData(0, 4400, 10, 5)]
    [InlineData(0, -720, 10, 5)]
    [InlineData(0, 0, 7, 5)]
    [InlineData(0, 0, 10, 3)]
    public void A_setting_out_of_range_is_refused(int bitrate, int maxResolution, int seekStep, int volumeStep)
    {
        var settings = Valid();
        settings.Bitrate = bitrate;
        settings.MaxResolution = maxResolution;
        settings.SeekStepSeconds = seekStep;
        settings.VolumeStepPercent = volumeStep;

        var act = () => DeviceSettings.From(settings);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 720)]
    [InlineData(40, 2160)]
    [InlineData(1000, 4320)]
    public void The_choices_the_apps_offer_are_accepted(int bitrate, int maxResolution)
    {
        var settings = Valid();
        settings.Bitrate = bitrate;
        settings.MaxResolution = maxResolution;

        DeviceSettings.From(settings).Bitrate.Should().Be(bitrate);
    }

    [Fact]
    public void The_shipped_settings_are_what_a_device_with_nothing_saved_always_got()
    {
        DeviceSettings.Shipped.Should().Be(new DeviceSettings(0, 0, 10, 5, true, true, true));
        DevicePlaybackBlob.Parse(null).Effective(DeviceSettings.Shipped).Settings.Should().Be(DeviceSettings.Shipped);
    }
}
