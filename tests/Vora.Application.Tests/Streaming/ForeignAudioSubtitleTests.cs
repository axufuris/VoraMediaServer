using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;

namespace Vora.Application.Tests.Streaming;

// The automatic subtitle pick decides what a viewer sees before they have
// touched anything, so every case here is a thing that happens without being
// asked for: the flag-driven baseline, and what the foreign-audio setting adds
// on top of it.
public class ForeignAudioSubtitleTests
{
    private static ServerSetting Settings(bool foreignAudioRule, string language = "eng") => new()
    {
        AutoEnableSubtitlesForForeignAudio = foreignAudioRule,
        MetadataLanguage = language
    };

    private static SubtitleStreamInfoDto Sub(string name, string? language, bool forced = false, bool sdh = false) => new()
    {
        Id = Guid.NewGuid(),
        Codec = name,
        Language = language,
        IsForced = forced,
        IsHearingImpaired = sdh
    };

    [Fact]
    public void With_the_rule_off_a_forced_track_is_still_what_plays()
    {
        var forced = Sub("forced", "eng", forced: true);
        var tracks = new List<SubtitleStreamInfoDto> { Sub("full", "eng"), forced };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "jpn", Settings(foreignAudioRule: false))
            .Should().Be(forced);
    }

    [Fact]
    public void With_the_rule_off_foreign_audio_changes_nothing()
    {
        var tracks = new List<SubtitleStreamInfoDto> { Sub("full", "eng") };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "jpn", Settings(foreignAudioRule: false))
            .Should().BeNull();
    }

    [Fact]
    public void Foreign_audio_turns_on_the_subtitle_in_the_servers_language()
    {
        var english = Sub("full", "eng");
        var tracks = new List<SubtitleStreamInfoDto> { Sub("japanese", "jpn"), english };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "jpn", Settings(foreignAudioRule: true))
            .Should().Be(english);
    }

    [Fact]
    public void A_forced_track_in_the_servers_language_is_preferred_to_the_full_one()
    {
        var forcedEnglish = Sub("forced", "eng", forced: true);
        var tracks = new List<SubtitleStreamInfoDto> { Sub("full", "eng"), forcedEnglish };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "fre", Settings(foreignAudioRule: true))
            .Should().Be(forcedEnglish);
    }

    [Fact]
    public void An_sdh_track_is_a_last_resort_rather_than_a_default()
    {
        var plain = Sub("full", "en");
        var tracks = new List<SubtitleStreamInfoDto> { Sub("sdh", "en", sdh: true), plain };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "kor", Settings(foreignAudioRule: true))
            .Should().Be(plain);
    }

    [Fact]
    public void An_sdh_track_is_taken_when_it_is_the_only_one_in_the_language()
    {
        var sdh = Sub("sdh", "eng", sdh: true);
        var tracks = new List<SubtitleStreamInfoDto> { Sub("japanese", "jpn"), sdh };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "jpn", Settings(foreignAudioRule: true))
            .Should().Be(sdh);
    }

    [Fact]
    public void Audio_already_in_the_servers_language_keeps_the_baseline()
    {
        var tracks = new List<SubtitleStreamInfoDto> { Sub("full", "eng") };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "eng", Settings(foreignAudioRule: true))
            .Should().BeNull();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ENG")]
    [InlineData("en-US")]
    [InlineData("en_GB")]
    public void The_same_language_written_differently_is_not_foreign(string audioLanguage)
    {
        var tracks = new List<SubtitleStreamInfoDto> { Sub("full", "eng") };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, audioLanguage, Settings(foreignAudioRule: true))
            .Should().BeNull();
    }

    // FFmpeg writes the bibliographic code into stream tags, the admin dropdown
    // stores the terminological one. "ger" audio on a German server is not a
    // foreign film.
    [Fact]
    public void A_bibliographic_code_matches_the_terminological_one()
    {
        var tracks = new List<SubtitleStreamInfoDto> { Sub("full", "deu") };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "ger", Settings(foreignAudioRule: true, language: "deu"))
            .Should().BeNull();
    }

    // ffprobe's tag reader stores "English", not "eng", and title-cases the rest
    // ("Jpn"). Those are the values actually on the rows this reads, so a matcher
    // that only understood codes would call every English track foreign.
    [Fact]
    public void The_display_names_the_analyzer_stores_are_matched_as_languages()
    {
        var tracks = new List<SubtitleStreamInfoDto> { Sub("full", "English") };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "English", Settings(foreignAudioRule: true))
            .Should().BeNull();
    }

    [Fact]
    public void A_display_name_on_the_audio_still_reads_as_foreign()
    {
        var english = Sub("full", "English");
        var tracks = new List<SubtitleStreamInfoDto> { Sub("japanese", "Jpn"), english };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "Jpn", Settings(foreignAudioRule: true))
            .Should().Be(english);
    }

    [Fact]
    public void Audio_with_no_language_tag_is_left_alone()
    {
        var tracks = new List<SubtitleStreamInfoDto> { Sub("full", "eng") };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, null, Settings(foreignAudioRule: true))
            .Should().BeNull();
    }

    [Fact]
    public void Foreign_audio_with_nothing_in_the_servers_language_falls_back_to_the_baseline()
    {
        var forcedJapanese = Sub("forced", "jpn", forced: true);
        var tracks = new List<SubtitleStreamInfoDto> { Sub("japanese", "jpn"), forcedJapanese };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "jpn", Settings(foreignAudioRule: true))
            .Should().Be(forcedJapanese);
    }

    [Fact]
    public void A_subtitle_with_no_language_is_never_assumed_to_be_the_servers()
    {
        var tracks = new List<SubtitleStreamInfoDto> { Sub("untagged", null) };

        BestPathDecisionManager.PickAutomaticSubtitle(tracks, "jpn", Settings(foreignAudioRule: true))
            .Should().BeNull();
    }
}
