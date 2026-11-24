using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class ClientDeviceSettingsEndpointTests : IClassFixture<VoraApiTestFactory>, IAsyncLifetime
{
    private const string ShippedDeviceDefaults = """{"device":{"bitrate":0,"maxResolution":0,"seekStepSeconds":10,"volumeStepPercent":5,"soundOnClick":true,"soundOnNavOpen":true,"soundOnMove":true}}""";
    private const string TwentyMbpsAndSkip30 = """{"device":{"bitrate":20,"maxResolution":1080,"seekStepSeconds":30,"volumeStepPercent":2,"soundOnClick":true,"soundOnNavOpen":true,"soundOnMove":false}}""";

    private readonly VoraApiTestFactory _factory;

    public ClientDeviceSettingsEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        using var admin = Client(Guid.NewGuid(), isAdmin: true);
        using var response = await PutAsync(admin, "/api/settings/client-defaults", ShippedDeviceDefaults);
        response.EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record Seeded(Guid AccountId, Guid ProfileId, string Tv, string Phone);

    private async Task<Seeded> SeedAsync(string? tvBlob = null)
    {
        var tv = $"tv-{Guid.NewGuid():N}";
        var phone = $"phone-{Guid.NewGuid():N}";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Parent" };
        var profile = new UserProfile { Name = "Kid", UserId = user.Id };
        db.AddRange(user, profile);
        db.Add(new ClientDevice { DeviceId = tv, DeviceName = "Living room", ClientName = "Vora Android", DeviceType = "TV", OperatingSystem = "Android", LastProfileId = profile.Id, LastConnectedAt = DateTime.UtcNow.AddHours(-2) });
        db.Add(new ClientDevice { DeviceId = phone, DeviceName = "Phone", ClientName = "Vora Android", DeviceType = "Phone", OperatingSystem = "Android", LastProfileId = profile.Id, LastConnectedAt = DateTime.UtcNow.AddHours(-1) });
        if (tvBlob is not null) db.Add(new ProfileDeviceSetting { ProfileId = profile.Id, DeviceId = tv, PlaybackPrefs = tvBlob });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Seeded(user.Id, profile.Id, tv, phone);
    }

    private HttpClient Client(Guid accountId, bool isAdmin = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueAccountToken(accountId, isAdmin));
        return client;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string url, string json) =>
        client.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> PlaybackAsync(HttpClient owner, Seeded seeded, string deviceId) =>
        await ReadAsync(await owner.GetAsync($"/api/users/profiles/{seeded.ProfileId}/devices/{deviceId}/playback", TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_device_with_nothing_saved_follows_the_servers_device_defaults_as_they_change()
    {
        var seeded = await SeedAsync();
        using var admin = Client(Guid.NewGuid(), isAdmin: true);
        using var owner = Client(seeded.AccountId);

        var before = await PlaybackAsync(owner, seeded, seeded.Phone);
        before.GetProperty("bitrate").GetInt32().Should().Be(0);
        before.GetProperty("seekStepSeconds").GetInt32().Should().Be(10);
        before.GetProperty("soundOnMove").GetBoolean().Should().BeTrue();

        (await PutAsync(admin, "/api/settings/client-defaults", TwentyMbpsAndSkip30)).EnsureSuccessStatusCode();

        var after = await PlaybackAsync(owner, seeded, seeded.Phone);
        after.GetProperty("bitrate").GetInt32().Should().Be(20);
        after.GetProperty("maxResolution").GetInt32().Should().Be(1080);
        after.GetProperty("seekStepSeconds").GetInt32().Should().Be(30);
        after.GetProperty("volumeStepPercent").GetInt32().Should().Be(2);
        after.GetProperty("soundOnMove").GetBoolean().Should().BeFalse();
        after.GetProperty("maxAudioChannels").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task A_value_the_device_saved_wins_over_the_default_even_when_it_is_zero_and_a_key_it_lacks_falls_to_the_default()
    {
        var seeded = await SeedAsync("""{"bitrate":0,"maxAudioChannels":6}""");
        using var admin = Client(Guid.NewGuid(), isAdmin: true);
        using var owner = Client(seeded.AccountId);
        (await PutAsync(admin, "/api/settings/client-defaults", TwentyMbpsAndSkip30)).EnsureSuccessStatusCode();

        var playback = await PlaybackAsync(owner, seeded, seeded.Tv);

        playback.GetProperty("bitrate").GetInt32().Should().Be(0);
        playback.GetProperty("maxAudioChannels").GetInt32().Should().Be(6);
        playback.GetProperty("maxResolution").GetInt32().Should().Be(1080);
        playback.GetProperty("seekStepSeconds").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task A_device_that_saves_for_itself_stops_following_the_defaults()
    {
        var seeded = await SeedAsync();
        using var admin = Client(Guid.NewGuid(), isAdmin: true);
        using var owner = Client(seeded.AccountId);
        (await PutAsync(admin, "/api/settings/client-defaults", TwentyMbpsAndSkip30)).EnsureSuccessStatusCode();

        using var saved = await PutAsync(owner, $"/api/users/profiles/{seeded.ProfileId}/devices/{seeded.Phone}/settings", """{"playbackPrefs":"{\"bitrate\":4,\"seekStepSeconds\":5}"}""");
        saved.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PutAsync(admin, "/api/settings/client-defaults", ShippedDeviceDefaults)).EnsureSuccessStatusCode();

        var playback = await PlaybackAsync(owner, seeded, seeded.Phone);
        playback.GetProperty("bitrate").GetInt32().Should().Be(4);
        playback.GetProperty("seekStepSeconds").GetInt32().Should().Be(5);
        playback.GetProperty("maxResolution").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task An_admin_lists_the_profiles_devices_with_what_each_one_uses()
    {
        var seeded = await SeedAsync("""{"bitrate":4,"seekStepSeconds":15}""");
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        var devices = await ReadAsync(await admin.GetAsync($"/api/users/profiles/{seeded.ProfileId}/device-settings", TestContext.Current.CancellationToken));

        var list = devices.EnumerateArray().ToList();
        list.Select(device => device.GetProperty("deviceId").GetString()).Should().Equal(seeded.Phone, seeded.Tv);
        var tv = list.Single(device => device.GetProperty("deviceId").GetString() == seeded.Tv);
        tv.GetProperty("name").GetString().Should().Be("Living room");
        tv.GetProperty("deviceType").GetString().Should().Be("TV");
        tv.GetProperty("settings").GetProperty("bitrate").GetInt32().Should().Be(4);
        tv.GetProperty("settings").GetProperty("seekStepSeconds").GetInt32().Should().Be(15);
        tv.GetProperty("settings").GetProperty("volumeStepPercent").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task An_admin_sets_one_device_and_the_others_are_left_alone()
    {
        var seeded = await SeedAsync();
        using var admin = Client(Guid.NewGuid(), isAdmin: true);
        using var owner = Client(seeded.AccountId);

        var saved = await ReadAsync(await PutAsync(admin, $"/api/users/profiles/{seeded.ProfileId}/device-settings",
            $$$"""{"deviceIds":["{{{seeded.Tv}}}"],"settings":{"bitrate":10,"maxResolution":720,"seekStepSeconds":5,"volumeStepPercent":10,"soundOnClick":false,"soundOnNavOpen":false,"soundOnMove":false}}"""));

        saved.EnumerateArray().Single(device => device.GetProperty("deviceId").GetString() == seeded.Tv).GetProperty("settings").GetProperty("bitrate").GetInt32().Should().Be(10);
        var tv = await PlaybackAsync(owner, seeded, seeded.Tv);
        tv.GetProperty("bitrate").GetInt32().Should().Be(10);
        tv.GetProperty("maxResolution").GetInt32().Should().Be(720);
        tv.GetProperty("soundOnClick").GetBoolean().Should().BeFalse();
        (await PlaybackAsync(owner, seeded, seeded.Phone)).GetProperty("bitrate").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task An_admin_applies_one_set_to_every_device_and_each_keeps_its_audio_layout_pick()
    {
        var seeded = await SeedAsync("""{"bitrate":4,"maxAudioChannels":6}""");
        using var admin = Client(Guid.NewGuid(), isAdmin: true);
        using var owner = Client(seeded.AccountId);

        (await PutAsync(admin, $"/api/users/profiles/{seeded.ProfileId}/device-settings",
            """{"settings":{"bitrate":40,"maxResolution":2160,"seekStepSeconds":30,"volumeStepPercent":2,"soundOnClick":true,"soundOnNavOpen":true,"soundOnMove":true}}""")).EnsureSuccessStatusCode();

        var tv = await PlaybackAsync(owner, seeded, seeded.Tv);
        var phone = await PlaybackAsync(owner, seeded, seeded.Phone);
        tv.GetProperty("bitrate").GetInt32().Should().Be(40);
        tv.GetProperty("maxAudioChannels").GetInt32().Should().Be(6);
        phone.GetProperty("bitrate").GetInt32().Should().Be(40);
        phone.GetProperty("maxResolution").GetInt32().Should().Be(2160);
    }

    [Fact]
    public async Task A_device_the_profile_has_not_used_is_refused_and_nothing_changes()
    {
        var seeded = await SeedAsync();
        using var admin = Client(Guid.NewGuid(), isAdmin: true);
        using var owner = Client(seeded.AccountId);

        using var response = await PutAsync(admin, $"/api/users/profiles/{seeded.ProfileId}/device-settings",
            $$$"""{"deviceIds":["{{{seeded.Tv}}}","a-stranger"],"settings":{"bitrate":10,"maxResolution":720,"seekStepSeconds":5,"volumeStepPercent":10,"soundOnClick":true,"soundOnNavOpen":true,"soundOnMove":true}}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PlaybackAsync(owner, seeded, seeded.Tv)).GetProperty("bitrate").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task A_setting_out_of_range_is_refused_for_a_device_and_for_the_defaults()
    {
        var seeded = await SeedAsync();
        using var admin = Client(Guid.NewGuid(), isAdmin: true);
        const string bad = """{"bitrate":10,"maxResolution":720,"seekStepSeconds":7,"volumeStepPercent":10,"soundOnClick":true,"soundOnNavOpen":true,"soundOnMove":true}""";

        using var device = await PutAsync(admin, $"/api/users/profiles/{seeded.ProfileId}/device-settings", $$"""{"settings":{{bad}}}""");
        using var defaults = await PutAsync(admin, "/api/settings/client-defaults", $$"""{"device":{{bad}}}""");

        device.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        defaults.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_an_admin_reads_or_sets_the_devices_of_a_profile()
    {
        var seeded = await SeedAsync();
        using var owner = Client(seeded.AccountId);

        using var read = await owner.GetAsync($"/api/users/profiles/{seeded.ProfileId}/device-settings", TestContext.Current.CancellationToken);
        using var write = await PutAsync(owner, $"/api/users/profiles/{seeded.ProfileId}/device-settings",
            """{"settings":{"bitrate":10,"maxResolution":720,"seekStepSeconds":5,"volumeStepPercent":10,"soundOnClick":true,"soundOnNavOpen":true,"soundOnMove":true}}""");

        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_profile_that_does_not_exist_is_not_found()
    {
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        using var response = await admin.GetAsync($"/api/users/profiles/{Guid.NewGuid()}/device-settings", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_profile_row_written_for_a_device_holds_only_the_playback_blob()
    {
        var seeded = await SeedAsync();
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        (await PutAsync(admin, $"/api/users/profiles/{seeded.ProfileId}/device-settings",
            $$$"""{"deviceIds":["{{{seeded.Phone}}}"],"settings":{"bitrate":10,"maxResolution":720,"seekStepSeconds":5,"volumeStepPercent":10,"soundOnClick":true,"soundOnNavOpen":true,"soundOnMove":true}}""")).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var row = await db.Set<ProfileDeviceSetting>().AsNoTracking().SingleAsync(s => s.ProfileId == seeded.ProfileId && s.DeviceId == seeded.Phone, TestContext.Current.CancellationToken);
        row.PlaybackPrefs.Should().Contain("\"bitrate\":10");
        row.IptvPrefsJson.Should().BeEmpty();
        row.NavPrefsJson.Should().BeEmpty();
    }
}
