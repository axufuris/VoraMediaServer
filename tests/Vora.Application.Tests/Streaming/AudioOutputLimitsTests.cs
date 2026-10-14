using Vora.Application.Streaming;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class AudioOutputLimitsTests
{
    private const StreamingProfile Direct = StreamingProfile.DirectStreamPreference;
    private const StreamingProfile ClientPreference = StreamingProfile.ClientPreference;
    private const StreamingProfile Bitrate = StreamingProfile.BandwidthOptimized;

    private static AudioOutputLimits TvApp(int detected, int picked, StreamingProfile profile) =>
        AudioOutputLimits.Resolve(Math.Min(detected, picked == 0 ? detected : picked), new AudioOutputRequest(detected, picked, PlaysAnyLayout: false, CanSwitchTracks: true), profile);

    private static AudioOutputLimits Browser(int detected, int picked, StreamingProfile profile) =>
        AudioOutputLimits.Resolve(detected, new AudioOutputRequest(detected, picked, PlaysAnyLayout: true, CanSwitchTracks: false), profile);

    [Theory]
    [InlineData(Direct)]
    [InlineData(ClientPreference)]
    [InlineData(Bitrate)]
    public void On_Automatic_a_TV_app_is_limited_to_and_aims_for_what_it_detected(StreamingProfile profile)
    {
        TvApp(detected: 6, picked: 0, profile).Should().Be(new AudioOutputLimits(Hard: 6, Target: 6, Pick: 0));
    }

    [Theory]
    [InlineData(Direct)]
    [InlineData(ClientPreference)]
    [InlineData(Bitrate)]
    public void A_pick_above_what_was_detected_raises_the_limit_in_every_profile(StreamingProfile profile)
    {
        TvApp(detected: 2, picked: 6, profile).Should().Be(new AudioOutputLimits(Hard: 6, Target: 6, Pick: 6));
    }

    [Fact]
    public void Under_Direct_a_lower_pick_is_a_preference_and_never_a_limit()
    {
        TvApp(detected: 6, picked: 2, Direct).Should().Be(new AudioOutputLimits(Hard: 6, Target: 2, Pick: 2));
    }

    [Theory]
    [InlineData(ClientPreference)]
    [InlineData(Bitrate)]
    public void Under_the_other_profiles_a_lower_pick_is_the_limit_for_a_TV_app(StreamingProfile profile)
    {
        TvApp(detected: 6, picked: 2, profile).Should().Be(new AudioOutputLimits(Hard: 2, Target: 2, Pick: 2));
    }

    [Theory]
    [InlineData(Direct, 0, 2)]
    [InlineData(Direct, 6, 6)]
    [InlineData(ClientPreference, 2, 2)]
    [InlineData(Bitrate, 6, 6)]
    public void A_browser_is_never_limited_because_it_mixes_down_itself(StreamingProfile profile, int picked, int target)
    {
        Browser(detected: 2, picked, profile).Should().Be(new AudioOutputLimits(Hard: 8, Target: target, Pick: picked));
    }

    [Theory]
    [InlineData(6, 6)]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    public void A_client_that_sends_only_the_older_single_value_is_limited_to_it(int reported, int expected)
    {
        AudioOutputLimits.Resolve(reported, null, ClientPreference).Should().Be(new AudioOutputLimits(expected, expected, 0));
        AudioOutputLimits.Resolve(reported, new AudioOutputRequest(0, 0, false, false), Direct).Should().Be(new AudioOutputLimits(expected, expected, 0));
    }

    [Theory]
    [InlineData(8, 8, true, "ac3", 6)]
    [InlineData(8, 6, true, "ac3", 6)]
    [InlineData(6, 6, false, "aac", 6)]
    [InlineData(8, 8, false, "aac", 8)]
    [InlineData(6, 2, true, "aac", 2)]
    [InlineData(2, 6, true, "aac", 2)]
    public void Converted_surround_is_Dolby_Digital_up_to_5_1_for_a_client_that_plays_it_and_AAC_otherwise(int trackChannels, int target, bool playsDolbyDigital, string codec, int channels)
    {
        new AudioOutputLimits(Hard: 8, target, Pick: 0).ConversionOf(trackChannels, playsDolbyDigital).Should().Be(new AudioConversion(codec, channels));
    }

    [Fact]
    public void Losing_surround_costs_more_than_converting_the_audio_but_missing_a_pick_does_not()
    {
        var surroundWanted = new AudioOutputLimits(Hard: 6, Target: 6, Pick: 0);
        var sevenOnePicked = new AudioOutputLimits(Hard: 8, Target: 8, Pick: 8);
        int conversion = AudioPlayback.ConversionPenalty + 100;

        int stereoInsteadOfSurround = surroundWanted.FitPenalty(2, 2, "aac", converted: false) - surroundWanted.FitPenalty(8, 6, "ac3", converted: true);
        int fiveOneInsteadOfSevenOne = sevenOnePicked.FitPenalty(6, 6, "ac3", converted: false) - sevenOnePicked.FitPenalty(8, 8, "aac", converted: true);

        stereoInsteadOfSurround.Should().BeGreaterThan(conversion);
        fiveOneInsteadOfSevenOne.Should().BeLessThan(conversion);
    }

    [Fact]
    public void Missing_an_explicit_pick_is_worth_repackaging_but_a_detected_layout_is_not()
    {
        var stereoPicked = new AudioOutputLimits(Hard: 8, Target: 2, Pick: 2);
        var stereoDetected = new AudioOutputLimits(Hard: 8, Target: 2, Pick: 0);

        int pickedGap = stereoPicked.FitPenalty(6, 6, "ac3", converted: false) - stereoPicked.FitPenalty(2, 2, "aac", converted: false);
        int detectedGap = stereoDetected.FitPenalty(8, 8, "eac3", converted: false) - stereoDetected.FitPenalty(2, 2, "aac", converted: false);

        pickedGap.Should().BeGreaterThan(AudioPlayback.RemuxPenalty);
        detectedGap.Should().BeLessThan(AudioPlayback.RemuxPenalty);
    }
}
