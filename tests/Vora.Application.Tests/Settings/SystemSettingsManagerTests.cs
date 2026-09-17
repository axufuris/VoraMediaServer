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
}
