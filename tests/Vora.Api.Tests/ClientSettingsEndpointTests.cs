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

public class ClientSettingsEndpointTests : IClassFixture<VoraApiTestFactory>, IAsyncLifetime
{
    private const string ShippedDefaults = """
        {"playback":{"autoSkipIntro":false,"autoSkipCredits":false,"autoPlayNext":true,"upNextOrder":"WhereILeftOff","episodeLayout":"Row","minimumCreditsSceneSeconds":15},
         "tracks":{"audioLanguage":null,"subtitleLanguage":null,"subtitleMode":null,"preferHearingImpairedSubtitles":false},
         "librarySort":{"sort":null,"descending":false}}
        """;

    private const string SpanishAndSkipping = """
        {"playback":{"autoSkipIntro":true,"autoSkipCredits":true,"autoPlayNext":false,"upNextOrder":"InOrder","episodeLayout":"List","minimumCreditsSceneSeconds":40},
         "tracks":{"audioLanguage":"spa","subtitleLanguage":"fra","subtitleMode":"Always","preferHearingImpairedSubtitles":true},
         "librarySort":{"sort":"DateAdded","descending":true}}
        """;

    private readonly VoraApiTestFactory _factory;

    public ClientSettingsEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        using var admin = Client(Guid.NewGuid(), isAdmin: true);
        using var response = await PutAsync(admin, "/api/settings/client-defaults", ShippedDefaults);
        response.EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<(Guid AccountId, Guid ProfileId)> SeedProfileAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Parent" };
        var profile = new UserProfile { Name = "Kid", UserId = user.Id };
        db.AddRange(user, profile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (user.Id, profile.Id);
    }

    private HttpClient Client(Guid accountId, bool isAdmin = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueAccountToken(accountId, isAdmin));
        return client;
    }

    private HttpClient ProfileClient(Guid accountId, Guid profileId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueProfileToken(accountId, profileId));
        return client;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string url, string json) =>
        client.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> CreateProfileThroughTheApiAsync(Guid accountId)
    {
        using var owner = Client(accountId);
        using var response = await owner.PostAsJsonAsync($"/api/users/{accountId}/profiles", new { name = "Created" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("profileId").GetGuid();
    }

    private async Task<UserProfile> ProfileAsync(Guid profileId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        return await db.Set<UserProfile>().AsNoTracking().SingleAsync(p => p.Id == profileId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_defaults_start_as_a_profile_starts()
    {
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        var defaults = await ReadAsync(await admin.GetAsync("/api/settings/client-defaults", TestContext.Current.CancellationToken));

        defaults.GetProperty("playback").GetProperty("autoPlayNext").GetBoolean().Should().BeTrue();
        defaults.GetProperty("playback").GetProperty("upNextOrder").GetString().Should().Be("WhereILeftOff");
        defaults.GetProperty("playback").GetProperty("minimumCreditsSceneSeconds").GetInt32().Should().Be(15);
        defaults.GetProperty("tracks").GetProperty("audioLanguage").ValueKind.Should().Be(JsonValueKind.Null);
        defaults.GetProperty("tracks").GetProperty("serverLanguage").GetString().Should().NotBeNullOrWhiteSpace();
        defaults.GetProperty("librarySort").GetProperty("sort").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Only_an_admin_reads_or_changes_the_defaults()
    {
        using var viewer = Client(Guid.NewGuid());

        using var read = await viewer.GetAsync("/api/settings/client-defaults", TestContext.Current.CancellationToken);
        using var write = await PutAsync(viewer, "/api/settings/client-defaults", SpanishAndSkipping);

        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_profile_created_after_the_defaults_change_starts_from_them_and_an_older_one_keeps_its_own()
    {
        var (accountId, olderProfileId) = await SeedProfileAsync();
        using var admin = Client(Guid.NewGuid(), isAdmin: true);
        (await PutAsync(admin, "/api/settings/client-defaults", SpanishAndSkipping)).EnsureSuccessStatusCode();

        var createdId = await CreateProfileThroughTheApiAsync(accountId);

        var created = await ProfileAsync(createdId);
        created.AutoSkipIntro.Should().BeTrue();
        created.AutoSkipCredits.Should().BeTrue();
        created.AutoPlayNext.Should().BeFalse();
        created.UpNextOrder.ToString().Should().Be("InOrder");
        created.EpisodeLayout.ToString().Should().Be("List");
        created.MinimumCreditsSceneSeconds.Should().Be(40);
        created.PreferredAudioLanguage.Should().Be("spa");
        created.PreferredSubtitleLanguage.Should().Be("fra");
        created.SubtitleMode.ToString().Should().Be("Always");
        created.PreferHearingImpairedSubtitles.Should().BeTrue();
        created.LibrarySort.ToString().Should().Be("DateAdded");
        created.LibrarySortDescending.Should().BeTrue();

        var older = await ProfileAsync(olderProfileId);
        older.AutoSkipIntro.Should().BeFalse();
        older.AutoPlayNext.Should().BeTrue();
        older.PreferredAudioLanguage.Should().BeNull();
        older.LibrarySort.Should().BeNull();
    }

    [Fact]
    public async Task A_bad_value_is_refused_and_leaves_the_defaults_as_they_were()
    {
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        using var response = await PutAsync(admin, "/api/settings/client-defaults", """{"tracks":{"audioLanguage":"spa"},"playback":{"upNextOrder":99}}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var defaults = await ReadAsync(await admin.GetAsync("/api/settings/client-defaults", TestContext.Current.CancellationToken));
        defaults.GetProperty("tracks").GetProperty("audioLanguage").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_admin_edits_a_profiles_settings_and_the_profile_reads_them_back_as_its_own()
    {
        var (accountId, profileId) = await SeedProfileAsync();
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        var saved = await ReadAsync(await PutAsync(admin, $"/api/users/profiles/{profileId}/client-settings", SpanishAndSkipping));

        saved.GetProperty("tracks").GetProperty("audioLanguage").GetString().Should().Be("spa");
        using var profile = ProfileClient(accountId, profileId);
        var playback = await ReadAsync(await profile.GetAsync("/api/users/profiles/me/playback-preferences", TestContext.Current.CancellationToken));
        playback.GetProperty("autoSkipCredits").GetBoolean().Should().BeTrue();
        playback.GetProperty("upNextOrder").GetString().Should().Be("InOrder");
        var tracks = await ReadAsync(await profile.GetAsync("/api/users/profiles/me/track-preferences", TestContext.Current.CancellationToken));
        tracks.GetProperty("subtitleMode").GetString().Should().Be("Always");
        var sort = await ReadAsync(await profile.GetAsync("/api/users/profiles/me/library-sort", TestContext.Current.CancellationToken));
        sort.GetProperty("sort").GetString().Should().Be("DateAdded");
        sort.GetProperty("descending").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task An_admin_reads_a_profiles_settings()
    {
        var (_, profileId) = await SeedProfileAsync();
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        var settings = await ReadAsync(await admin.GetAsync($"/api/users/profiles/{profileId}/client-settings", TestContext.Current.CancellationToken));

        settings.GetProperty("playback").GetProperty("autoPlayNext").GetBoolean().Should().BeTrue();
        settings.GetProperty("tracks").GetProperty("serverSubtitleMode").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_account_that_owns_a_profile_cannot_use_the_admin_route()
    {
        var (accountId, profileId) = await SeedProfileAsync();
        using var owner = Client(accountId);

        using var read = await owner.GetAsync($"/api/users/profiles/{profileId}/client-settings", TestContext.Current.CancellationToken);
        using var write = await PutAsync(owner, $"/api/users/profiles/{profileId}/client-settings", SpanishAndSkipping);

        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProfileAsync(profileId)).PreferredAudioLanguage.Should().BeNull();
    }

    [Fact]
    public async Task A_profile_that_does_not_exist_is_not_found()
    {
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        using var read = await admin.GetAsync($"/api/users/profiles/{Guid.NewGuid()}/client-settings", TestContext.Current.CancellationToken);
        using var write = await PutAsync(admin, $"/api/users/profiles/{Guid.NewGuid()}/client-settings", SpanishAndSkipping);

        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        write.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_bad_value_in_an_admin_edit_changes_nothing_on_the_profile()
    {
        var (_, profileId) = await SeedProfileAsync();
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        using var response = await PutAsync(admin, $"/api/users/profiles/{profileId}/client-settings", """{"tracks":{"audioLanguage":"spa"},"librarySort":{"sort":99}}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProfileAsync(profileId)).PreferredAudioLanguage.Should().BeNull();
    }
}
