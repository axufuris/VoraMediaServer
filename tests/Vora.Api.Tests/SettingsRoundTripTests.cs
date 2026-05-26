using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Api.Tests.Infra;
using Vora.Application.Settings.ViewModels;

namespace Vora.Api.Tests;

public class SettingsRoundTripTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public SettingsRoundTripTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AdminClient()
    {
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GET_server_settings_returns_default_shape()
    {
        var client = AdminClient();

        var response = await client.GetAsync("/api/settings/server");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await response.Content.ReadFromJsonAsync<ServerSettingsVM>();
        settings.Should().NotBeNull();
        settings!.ServerName.Should().NotBeNullOrEmpty();
        settings.NightlyScanTime.Should().MatchRegex(@"^\d{2}:\d{2}$");
    }

    [Fact]
    public async Task PUT_then_GET_server_settings_preserves_changed_fields()
    {
        var client = AdminClient();

        var current = await client.GetFromJsonAsync<ServerSettingsVM>("/api/settings/server");
        current.Should().NotBeNull();

        current!.ServerName = "Round-Trip Server";
        current.NightlyScanTime = "03:30";
        current.DvrPreRollSeconds = 90;
        current.DailyMixCount = 8;

        var put = await client.PutAsJsonAsync("/api/settings/server", current);
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshed = await client.GetFromJsonAsync<ServerSettingsVM>("/api/settings/server");
        refreshed!.ServerName.Should().Be("Round-Trip Server");
        refreshed.NightlyScanTime.Should().Be("03:30");
        refreshed.DvrPreRollSeconds.Should().Be(90);
        refreshed.DailyMixCount.Should().Be(8);
    }

    [Fact]
    public async Task PUT_server_settings_clamps_video_thumbnail_width_to_max_1280()
    {
        var client = AdminClient();

        var current = await client.GetFromJsonAsync<ServerSettingsVM>("/api/settings/server");
        current!.VideoThumbnailWidth = 9999;

        await client.PutAsJsonAsync("/api/settings/server", current);
        var refreshed = await client.GetFromJsonAsync<ServerSettingsVM>("/api/settings/server");

        refreshed!.VideoThumbnailWidth.Should().Be(1280);
    }

    [Fact]
    public async Task PUT_server_settings_clamps_silence_threshold_offset_to_min_minus_40()
    {
        var client = AdminClient();

        var current = await client.GetFromJsonAsync<ServerSettingsVM>("/api/settings/server");
        current!.SilenceThresholdOffsetDb = -999;

        await client.PutAsJsonAsync("/api/settings/server", current);
        var refreshed = await client.GetFromJsonAsync<ServerSettingsVM>("/api/settings/server");

        refreshed!.SilenceThresholdOffsetDb.Should().Be(-40);
    }

    [Fact]
    public async Task PUT_server_settings_clamps_daily_mix_drift_percent()
    {
        var client = AdminClient();

        var current = await client.GetFromJsonAsync<ServerSettingsVM>("/api/settings/server");
        current!.DailyMixDriftPercent = 250;

        await client.PutAsJsonAsync("/api/settings/server", current);
        var refreshed = await client.GetFromJsonAsync<ServerSettingsVM>("/api/settings/server");

        refreshed!.DailyMixDriftPercent.Should().Be(100);
    }

    [Fact]
    public async Task PUT_server_settings_blank_transcoder_temp_directory_defaults_to_transcode()
    {
        var client = AdminClient();

        var current = await client.GetFromJsonAsync<ServerSettingsVM>("/api/settings/server");
        current!.TranscoderTempDirectory = "   ";

        await client.PutAsJsonAsync("/api/settings/server", current);
        var refreshed = await client.GetFromJsonAsync<ServerSettingsVM>("/api/settings/server");

        refreshed!.TranscoderTempDirectory.Should().Be("/transcode");
    }

    [Fact]
    public async Task Feature_flags_PUT_then_GET_round_trips_each_flag()
    {
        var client = AdminClient();

        var put = await client.PutAsJsonAsync("/api/settings/features", new UpdateFeatureFlagsRequest
        {
            Discover = false,
            ForYou = false,
            ReleaseCalendar = true,
            LiveTv = false,
            Dvr = false,
            InternetRadio = true,
            Podcasts = false
        });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var flags = await client.GetFromJsonAsync<FeatureFlagsVM>("/api/server/features");
        flags.Should().NotBeNull();
        flags!.Discover.Should().BeFalse();
        flags.ForYou.Should().BeFalse();
        flags.ReleaseCalendar.Should().BeTrue();
        flags.LiveTv.Should().BeFalse();
        flags.Dvr.Should().BeFalse();
        flags.InternetRadio.Should().BeTrue();
        flags.Podcasts.Should().BeFalse();
    }

    [Fact]
    public async Task Non_admin_user_cannot_read_server_settings()
    {
        var profileToken = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", profileToken);

        var response = await client.GetAsync("/api/settings/server");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
