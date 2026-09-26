using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Settings;
using Vora.Application.Settings.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Settings;

public class SystemSettingsManagerTests
{
    private readonly ISystemSettingsRepository _repo;
    private readonly ServiceCollection _services;

    public SystemSettingsManagerTests()
    {
        _repo = Substitute.For<ISystemSettingsRepository>();
        _services = new ServiceCollection();
    }

    private SystemSettingsManager Build(params IVoraPlugin[] plugins) =>
        new(_repo, plugins, _services.BuildServiceProvider());

    private static ServerSettingsVM ValidRequest() => new()
    {
        ServerName = "Vora-One",
        NightlyScanTime = "02:30",
        DetectionScheduleTime = "04:00",
        VideoThumbnailScheduleTime = "05:00",
        DvrConflictPolicy = "AlwaysRecord",
        TonemappingAlgorithm = "hable",
        TranscoderTempDirectory = "/tmp",
        HardwareTranscodingDevice = "Auto",
        FolderWatcherProviderId = "polling_watcher",
        LocalMediaScannerProviderId = "Vora_scanner",
        DailyMixSchedule = "Daily3am"
    };

    // ---------- Clamping & coercion ----------

    [Fact]
    public async Task UpdateServerSettingsAsync_clamps_silence_threshold_offset_to_min_minus_40()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.SilenceThresholdOffsetDb = -999;

        await Build().UpdateServerSettingsAsync(req);

        settings.SilenceThresholdOffsetDb.Should().Be(-40);
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_clamps_silence_threshold_offset_to_max_zero()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.SilenceThresholdOffsetDb = 50;

        await Build().UpdateServerSettingsAsync(req);

        settings.SilenceThresholdOffsetDb.Should().Be(0);
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_clamps_thumbnail_width_to_80_to_1280_range()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.VideoThumbnailWidth = 9999;

        await Build().UpdateServerSettingsAsync(req);

        settings.VideoThumbnailWidth.Should().Be(1280);
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_clamps_thumbnail_height_to_45_floor()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.VideoThumbnailHeight = 10;

        await Build().UpdateServerSettingsAsync(req);

        settings.VideoThumbnailHeight.Should().Be(45);
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_clamps_episode_cluster_min_agreement_to_50_floor()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.EpisodeIntroClusterMinAgreementPct = 10;

        await Build().UpdateServerSettingsAsync(req);

        settings.EpisodeIntroClusterMinAgreementPct.Should().Be(50);
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_clamps_daily_mix_drift_percent_to_0_100()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.DailyMixDriftPercent = 250;

        await Build().UpdateServerSettingsAsync(req);

        settings.DailyMixDriftPercent.Should().Be(100);
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_floors_dvr_max_storage_to_zero()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.DvrMaxStorageGb = -50;

        await Build().UpdateServerSettingsAsync(req);

        settings.DvrMaxStorageGb.Should().Be(0);
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_clamps_dvr_storage_warning_percent_to_100()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.DvrStorageWarningPercent = 150;

        await Build().UpdateServerSettingsAsync(req);

        settings.DvrStorageWarningPercent.Should().Be(100);
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_defaults_transcoder_temp_directory_when_blank()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.TranscoderTempDirectory = "   ";

        await Build().UpdateServerSettingsAsync(req);

        settings.TranscoderTempDirectory.Should().Be("/transcode");
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_defaults_hardware_device_when_blank()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.HardwareTranscodingDevice = "";

        await Build().UpdateServerSettingsAsync(req);

        settings.HardwareTranscodingDevice.Should().Be("Auto");
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_defaults_tonemapping_algorithm_when_blank()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.TonemappingAlgorithm = "";

        await Build().UpdateServerSettingsAsync(req);

        settings.TonemappingAlgorithm.Should().Be("hable");
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_defaults_daily_mix_schedule_when_blank()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.DailyMixSchedule = "";

        await Build().UpdateServerSettingsAsync(req);

        settings.DailyMixSchedule.Should().Be("Daily3am");
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_parses_dvr_conflict_policy_case_insensitively()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.DvrConflictPolicy = "dropnewest";

        await Build().UpdateServerSettingsAsync(req);

        settings.DvrConflictPolicy.Should().Be(DvrConflictPolicy.DropNewest);
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_leaves_dvr_conflict_policy_unchanged_when_invalid()
    {
        var settings = new ServerSetting { DvrConflictPolicy = DvrConflictPolicy.AlwaysRecord };
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.DvrConflictPolicy = "bogus";

        await Build().UpdateServerSettingsAsync(req);

        settings.DvrConflictPolicy.Should().Be(DvrConflictPolicy.AlwaysRecord);
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_persists_via_save_changes()
    {
        _repo.GetSettingsForUpdateAsync().Returns(new ServerSetting());

        await Build().UpdateServerSettingsAsync(ValidRequest());

        await _repo.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_dvr_storage_path_blank_persists_as_null()
    {
        var settings = new ServerSetting { DvrStoragePath = "/old/path" };
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.DvrStoragePath = "   ";

        await Build().UpdateServerSettingsAsync(req);

        settings.DvrStoragePath.Should().BeNull();
    }

    [Fact]
    public async Task UpdateServerSettingsAsync_writes_through_enum_fields_intact()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        var req = ValidRequest();
        req.RunDetections = (int)DetectionTrigger.OnAdditionAndSchedule;
        req.RegistrationMode = (int)RegistrationMode.SecretWord;

        await Build().UpdateServerSettingsAsync(req);

        settings.RunDetections.Should().Be(DetectionTrigger.OnAdditionAndSchedule);
        settings.RegistrationMode.Should().Be(RegistrationMode.SecretWord);
    }

    // ---------- Plugin settings ----------

    [Fact]
    public async Task GetPluginSettingsAsync_returns_empty_list_for_unknown_plugin()
    {
        var result = await Build().GetPluginSettingsAsync("nope");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPluginSettingsAsync_always_includes_is_enabled_field_first()
    {
        var plugin = Substitute.For<IVoraPlugin>();
        plugin.Id.Returns("p1");
        plugin.GetSettingDefinitions().Returns(new List<PluginSettingDefinitionDto>());
        _repo.GetAllPluginSettingsAsync("p1").Returns(new Dictionary<string, string>());

        var result = await Build(plugin).GetPluginSettingsAsync("p1");

        result.Should().NotBeEmpty();
        result[0].Key.Should().Be("is_enabled");
        result[0].Type.Should().Be("boolean");
        result[0].Value.Should().Be("true");
    }

    [Fact]
    public async Task GetPluginSettingsAsync_uses_saved_is_enabled_value_when_present()
    {
        var plugin = Substitute.For<IVoraPlugin>();
        plugin.Id.Returns("p1");
        plugin.GetSettingDefinitions().Returns(new List<PluginSettingDefinitionDto>());
        _repo.GetAllPluginSettingsAsync("p1").Returns(new Dictionary<string, string> { ["is_enabled"] = "false" });

        var result = await Build(plugin).GetPluginSettingsAsync("p1");

        result[0].Key.Should().Be("is_enabled");
        result[0].Value.Should().Be("false");
    }

    [Fact]
    public async Task GetPluginSettingsAsync_maps_definitions_with_saved_values_overriding_defaults()
    {
        var plugin = Substitute.For<IVoraPlugin>();
        plugin.Id.Returns("p1");
        plugin.GetSettingDefinitions().Returns(new List<PluginSettingDefinitionDto>
        {
            new() { Key = "api_key", Label = "API Key", Type = "password", DefaultValue = "" },
            new() { Key = "region", Label = "Region", Type = "text", DefaultValue = "US" }
        });
        _repo.GetAllPluginSettingsAsync("p1").Returns(new Dictionary<string, string>
        {
            ["api_key"] = "saved-key"
        });

        var result = await Build(plugin).GetPluginSettingsAsync("p1");

        var apiKey = result.Single(f => f.Key == "api_key");
        apiKey.Value.Should().Be("saved-key");
        var region = result.Single(f => f.Key == "region");
        region.Value.Should().Be("US"); // fell back to default
    }

    [Fact]
    public async Task UpdatePluginSettingsAsync_writes_each_key_separately()
    {
        await Build().UpdatePluginSettingsAsync("p1", new Dictionary<string, string>
        {
            ["api_key"] = "k",
            ["region"] = "CA"
        });

        await _repo.Received(1).SetPluginSettingAsync("p1", "api_key", "k");
        await _repo.Received(1).SetPluginSettingAsync("p1", "region", "CA");
    }

    // ---------- Feature flags ----------

    // With every source configured, each flag is just its stored toggle — which
    // is what the derived ones must still reduce to once nothing is missing.
    [Fact]
    public async Task GetFeatureFlagsAsync_maps_all_seven_flags_from_settings()
    {
        _repo.GetSettingsAsync().Returns(new ServerSetting
        {
            EnableDiscover = true,
            EnableForYou = false,
            EnableReleaseCalendar = true,
            EnableLiveTv = false,
            EnableDvr = true,
            EnableInternetRadio = false,
            EnablePodcasts = true
        });
        WithDiscoveryProvider(configured: true);
        WithIptvSources(tv: true, radio: true);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.Discover.Should().BeTrue();
        vm.ForYou.Should().BeFalse();
        vm.ReleaseCalendar.Should().BeTrue();
        vm.LiveTv.Should().BeFalse();
        vm.Dvr.Should().BeTrue();
        vm.InternetRadio.Should().BeFalse();
        vm.Podcasts.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateFeatureFlagsAsync_writes_all_seven_flags_and_persists()
    {
        var settings = new ServerSetting();
        _repo.GetSettingsForUpdateAsync().Returns(settings);

        await Build().UpdateFeatureFlagsAsync(new UpdateFeatureFlagsRequest
        {
            Discover = false,
            ForYou = false,
            ReleaseCalendar = false,
            LiveTv = false,
            Dvr = false,
            InternetRadio = false,
            Podcasts = false
        });

        settings.EnableDiscover.Should().BeFalse();
        settings.EnableForYou.Should().BeFalse();
        settings.EnableReleaseCalendar.Should().BeFalse();
        settings.EnableLiveTv.Should().BeFalse();
        settings.EnableDvr.Should().BeFalse();
        settings.EnableInternetRadio.Should().BeFalse();
        settings.EnablePodcasts.Should().BeFalse();
        await _repo.Received(1).SaveChangesAsync();
    }

    // ---------- Live TV / Internet Radio are derived, not just stored ----------

    private void WithIptvSources(bool tv, bool radio)
    {
        var iptv = Substitute.For<Vora.Application.Iptv.IIptvRepository>();
        iptv.GetSourceAvailabilityAsync(Arg.Any<CancellationToken>()).Returns((tv, radio));
        _services.AddSingleton(iptv);
    }

    private void WithDiscoveryProvider(bool configured)
    {
        var discovery = Substitute.For<Vora.Application.Discovery.IDiscoveryManager>();
        discovery.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(configured);
        _services.AddSingleton(discovery);
    }

    private void EverythingSwitchedOn() => _repo.GetSettingsAsync().Returns(new ServerSetting
    {
        EnableDiscover = true,
        EnableForYou = true,
        EnableReleaseCalendar = true,
        EnableLiveTv = true,
        EnableDvr = true,
        EnableInternetRadio = true,
        EnablePodcasts = true
    });

    // The whole point: a nav entry that opens an empty page is worse than none.
    [Fact]
    public async Task GetFeatureFlagsAsync_reports_live_tv_off_when_it_is_on_but_unconfigured()
    {
        EverythingSwitchedOn();
        WithIptvSources(tv: false, radio: false);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.LiveTv.Should().BeFalse();
        vm.InternetRadio.Should().BeFalse();
    }

    [Fact]
    public async Task GetFeatureFlagsAsync_reports_live_tv_on_once_a_tv_source_exists()
    {
        EverythingSwitchedOn();
        WithIptvSources(tv: true, radio: false);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.LiveTv.Should().BeTrue();
        vm.InternetRadio.Should().BeFalse();
    }

    [Fact]
    public async Task GetFeatureFlagsAsync_derives_radio_from_its_own_sources()
    {
        EverythingSwitchedOn();
        WithIptvSources(tv: false, radio: true);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.InternetRadio.Should().BeTrue();
        vm.LiveTv.Should().BeFalse();
    }

    [Fact]
    public async Task GetFeatureFlagsAsync_lets_the_admin_toggle_win_over_a_configured_source()
    {
        _repo.GetSettingsAsync().Returns(new ServerSetting { EnableLiveTv = false, EnableInternetRadio = false });
        WithIptvSources(tv: true, radio: true);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.LiveTv.Should().BeFalse();
        vm.InternetRadio.Should().BeFalse();
    }

    // The admin screens need the saved choice, or a server with no playlist
    // shows the switch off and flipping it appears to do nothing.
    [Fact]
    public async Task GetFeatureFlagsAsync_reports_the_stored_toggles_alongside_the_derived_flags()
    {
        EverythingSwitchedOn();
        WithIptvSources(tv: false, radio: false);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.LiveTvEnabled.Should().BeTrue();
        vm.InternetRadioEnabled.Should().BeTrue();
        vm.DiscoverEnabled.Should().BeTrue();
        vm.LiveTv.Should().BeFalse();
        vm.InternetRadio.Should().BeFalse();
        vm.Discover.Should().BeFalse();
    }

    // For You, DVR and podcasts have no unconfigured state worth detecting, and
    // podcasts would be unreachable if they did — a subscription is made from
    // the page the flag would hide.
    //
    // The release calendar looks like a candidate and is not: its providers
    // include LocalCalendarProvider, which builds events out of the library and
    // needs no configuration, so deriving it from Radarr/Sonarr would hide a
    // working calendar from everyone who doesn't run them.
    [Fact]
    public async Task GetFeatureFlagsAsync_leaves_every_other_flag_as_the_plain_toggle()
    {
        EverythingSwitchedOn();
        WithIptvSources(tv: false, radio: false);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.ForYou.Should().BeTrue();
        vm.ReleaseCalendar.Should().BeTrue();
        vm.Dvr.Should().BeTrue();
        vm.Podcasts.Should().BeTrue();
    }

    // Every row on the Discover page comes from a provider, and a provider with
    // no key returns none — so an unconfigured server's Discover page is blank.
    [Fact]
    public async Task GetFeatureFlagsAsync_reports_discover_off_when_no_provider_has_a_key()
    {
        EverythingSwitchedOn();
        WithDiscoveryProvider(configured: false);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.Discover.Should().BeFalse();
        vm.DiscoverEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetFeatureFlagsAsync_reports_discover_on_once_a_key_is_entered()
    {
        EverythingSwitchedOn();
        WithDiscoveryProvider(configured: true);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.Discover.Should().BeTrue();
    }

    [Fact]
    public async Task GetFeatureFlagsAsync_lets_the_discover_toggle_win_over_a_configured_provider()
    {
        _repo.GetSettingsAsync().Returns(new ServerSetting { EnableDiscover = false });
        WithDiscoveryProvider(configured: true);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.Discover.Should().BeFalse();
    }

    // A configured discovery provider says nothing about the calendar: they do
    // not share a source.
    [Fact]
    public async Task GetFeatureFlagsAsync_does_not_tie_the_release_calendar_to_discovery()
    {
        EverythingSwitchedOn();
        WithDiscoveryProvider(configured: false);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.ReleaseCalendar.Should().BeTrue();
        vm.Discover.Should().BeFalse();
    }

    [Fact]
    public async Task GetFeatureFlagsAsync_treats_a_missing_iptv_service_as_nothing_configured()
    {
        EverythingSwitchedOn();

        var vm = await Build().GetFeatureFlagsAsync();

        vm.LiveTv.Should().BeFalse();
        vm.InternetRadio.Should().BeFalse();
        vm.LiveTvEnabled.Should().BeTrue();
    }

    // The derivation is read-side only. An admin saving while their source is
    // unreachable must not have the stored choice rewritten to false.
    [Fact]
    public async Task UpdateFeatureFlagsAsync_stores_what_the_admin_sent_whatever_the_sources_say()
    {
        var settings = new ServerSetting { EnableLiveTv = false, EnableInternetRadio = false };
        _repo.GetSettingsForUpdateAsync().Returns(settings);
        _repo.GetSettingsAsync().Returns(settings);
        WithIptvSources(tv: false, radio: false);
        var manager = Build();

        await manager.UpdateFeatureFlagsAsync(new UpdateFeatureFlagsRequest { LiveTv = true, InternetRadio = true });

        settings.EnableLiveTv.Should().BeTrue();
        settings.EnableInternetRadio.Should().BeTrue();

        var vm = await manager.GetFeatureFlagsAsync();
        vm.LiveTvEnabled.Should().BeTrue();
        vm.LiveTv.Should().BeFalse();
    }

    // AI playlists are off until an admin turns them on, and then usable only
    // with For You on and an OpenAI key to make them with.
    [Theory]
    [InlineData(true, true, "sk-key", true, true)]
    [InlineData(false, true, "sk-key", true, false)]
    [InlineData(true, false, "sk-key", true, false)]
    [InlineData(true, true, null, true, false)]
    public async Task AI_playlists_need_the_toggle_For_You_and_a_key(bool toggle, bool forYou, string? key, bool requests, bool expected)
    {
        _repo.GetSettingsAsync().Returns(new ServerSetting { EnableAiMusicPlaylists = toggle, EnableForYou = forYou, EnableAiPlaylistRequests = requests });
        _repo.GetPluginSettingAsync("openai_recommendations", "api_key").Returns(key);

        var vm = await Build().GetFeatureFlagsAsync();

        vm.AiPlaylists.Should().Be(expected);
        vm.AiPlaylistRequests.Should().Be(expected);
    }

    [Fact]
    public async Task The_make_me_a_playlist_box_has_its_own_off_switch()
    {
        _repo.GetSettingsAsync().Returns(new ServerSetting { EnableAiMusicPlaylists = true, EnableForYou = true, EnableAiPlaylistRequests = false });
        _repo.GetPluginSettingAsync("openai_recommendations", "api_key").Returns("sk-key");

        var vm = await Build().GetFeatureFlagsAsync();

        vm.AiPlaylists.Should().BeTrue();
        vm.AiPlaylistRequests.Should().BeFalse();
    }

    [Fact]
    public void AI_playlists_start_off_and_requests_start_on_behind_them()
    {
        var fresh = new ServerSetting();

        fresh.EnableAiMusicPlaylists.Should().BeFalse();
        fresh.EnableAiPlaylistRequests.Should().BeTrue();
        fresh.AiPlaylistRequestsPerDay.Should().Be(10);
    }
}
