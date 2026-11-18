using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vora.Api.Tests.Infra;
using Vora.Application.Auth.Pairing;
using Vora.Application.Settings;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class DevicePairingEndpointTests(VoraApiTestFactory factory) : IClassFixture<VoraApiTestFactory>
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    private readonly ManualClock _clock = new();

    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IDevicePairingStore>();
        services.AddSingleton<IDevicePairingStore>(new DevicePairingStore(_clock));
    }));

    private static async Task<T> InScopeAsync<T>(WebApplicationFactory<Program> host, Func<IServiceProvider, VoraDbContext, Task<T>> action)
    {
        using var scope = host.Services.CreateScope();
        return await action(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<VoraDbContext>());
    }

    private static Task<User> AddUserAsync(WebApplicationFactory<Program> host, string email = "sam@example.com", bool isAdmin = false) =>
        InScopeAsync(host, async (_, db) =>
        {
            var user = new User
            {
                Email = email,
                DisplayName = "Sam",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("ValidPassword123"),
                IsAdmin = isAdmin
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return user;
        });

    private static Task TurnPasswordSignInOffAsync(WebApplicationFactory<Program> host) =>
        InScopeAsync(host, async (services, _) =>
        {
            var repository = services.GetRequiredService<ISystemSettingsRepository>();
            var settings = await repository.GetSettingsForUpdateAsync();
            settings.PasswordSignInEnabled = false;
            await repository.SaveChangesAsync();
            return 0;
        });

    private static Task BlockDeviceAsync(WebApplicationFactory<Program> host, string deviceId) =>
        InScopeAsync(host, async (_, db) =>
        {
            db.ClientDevices.Add(new ClientDevice { DeviceId = deviceId, IsBlocked = true });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return 0;
        });

    private static HttpClient Tv(WebApplicationFactory<Program> host, string? deviceId = "tv-1")
    {
        var client = host.CreateClient();
        if (deviceId is not null) client.DefaultRequestHeaders.Add("X-Vora-Device-Id", deviceId);
        client.DefaultRequestHeaders.Add("X-Vora-Client", "Vora Roku");
        client.DefaultRequestHeaders.Add("X-Vora-Device", "Living Room");
        client.DefaultRequestHeaders.Add("X-Vora-Device-Type", "TV");
        client.DefaultRequestHeaders.Add("X-Vora-OS", "Roku OS");
        return client;
    }

    private static HttpClient Browser(WebApplicationFactory<Program> host, User user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueProfileToken(user.Id, Guid.NewGuid(), user.IsAdmin));
        return client;
    }

    private static async Task<JsonNode> StartAsync(HttpClient tv)
    {
        using var response = await tv.PostAsync("/api/auth/device/start", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<JsonNode> PollAsync(HttpClient tv, string deviceCode)
    {
        using var response = await tv.PostAsJsonAsync("/api/auth/device/poll", new { deviceCode }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken))?["title"]?.GetValue<string>();

    [Fact]
    public async Task Starting_hands_the_device_a_code_to_show_and_an_address_to_open()
    {
        using var host = Host();

        var pairing = await StartAsync(Tv(host));

        pairing["userCode"]!.GetValue<string>().Should().MatchRegex("^[BCDFGHJKMNPQRSTVWXZ2-9]{4}-[BCDFGHJKMNPQRSTVWXZ2-9]{4}$");
        pairing["deviceCode"]!.GetValue<string>().Length.Should().BeGreaterThan(40);
        pairing["verificationUrl"]!.GetValue<string>().Should().EndWith("/link");
        pairing["verificationUrlComplete"]!.GetValue<string>().Should().Contain("/link?code=");
        pairing["expiresInSeconds"]!.GetValue<int>().Should().Be(600);
        pairing["intervalSeconds"]!.GetValue<int>().Should().Be(5);
    }

    [Fact]
    public async Task Approving_signs_the_device_in_as_the_account_that_approved_and_only_once()
    {
        using var host = Host();
        var tv = Tv(host, deviceId: null);
        var approver = await AddUserAsync(host);
        var pairing = await StartAsync(tv);
        var deviceCode = pairing["deviceCode"]!.GetValue<string>();
        (await PollAsync(tv, deviceCode))["status"]!.GetValue<string>().Should().Be("pending");

        using var approval = await Browser(host, approver).PostAsJsonAsync("/api/auth/device/approve", new { userCode = pairing["userCode"]!.GetValue<string>() }, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(5));
        var approved = await PollAsync(tv, deviceCode);
        _clock.Advance(TimeSpan.FromSeconds(5));
        var again = await PollAsync(tv, deviceCode);

        approval.StatusCode.Should().Be(HttpStatusCode.OK);
        approved["status"]!.GetValue<string>().Should().Be("approved");
        approved["auth"]!["accessToken"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        approved["auth"]!["refreshToken"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        approved["auth"]!["userId"]!.GetValue<Guid>().Should().Be(approver.Id);
        again["status"]!.GetValue<string>().Should().Be("expired");
        again["auth"].Should().BeNull();
    }

    [Fact]
    public async Task The_page_is_shown_the_device_that_is_asking()
    {
        using var host = Host();
        var user = await AddUserAsync(host);
        var pairing = await StartAsync(Tv(host));

        using var response = await Browser(host, user).GetAsync($"/api/auth/device/lookup?userCode={pairing["userCode"]!.GetValue<string>()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var request = (await response.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken))!;
        request["clientName"]!.GetValue<string>().Should().Be("Vora Roku");
        request["deviceName"]!.GetValue<string>().Should().Be("Living Room");
        request["operatingSystem"]!.GetValue<string>().Should().Be("Roku OS");
        request["deviceType"]!.GetValue<string>().Should().Be("TV");
        request["location"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        request["requestedAt"].Should().NotBeNull();
    }

    [Fact]
    public async Task Denying_gives_the_device_no_session()
    {
        using var host = Host();
        var tv = Tv(host);
        var user = await AddUserAsync(host);
        var pairing = await StartAsync(tv);

        using var denial = await Browser(host, user).PostAsJsonAsync("/api/auth/device/deny", new { userCode = pairing["userCode"]!.GetValue<string>() }, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(5));
        var polled = await PollAsync(tv, pairing["deviceCode"]!.GetValue<string>());

        denial.StatusCode.Should().Be(HttpStatusCode.OK);
        polled["status"]!.GetValue<string>().Should().Be("denied");
        polled["auth"].Should().BeNull();
    }

    [Fact]
    public async Task Looking_up_or_answering_needs_a_signed_in_account()
    {
        using var host = Host();
        var anonymous = host.CreateClient();

        using var lookup = await anonymous.GetAsync("/api/auth/device/lookup?userCode=BCDFGHJK", TestContext.Current.CancellationToken);
        using var approve = await anonymous.PostAsJsonAsync("/api/auth/device/approve", new { userCode = "BCDFGHJK" }, TestContext.Current.CancellationToken);

        lookup.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        approve.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_code_that_matches_nothing_is_not_found_and_five_of_them_lock_the_account_out()
    {
        using var host = Host();
        var user = await AddUserAsync(host);
        var pairing = await StartAsync(Tv(host));
        var browser = Browser(host, user);

        for (var i = 0; i < 5; i++)
        {
            using var wrong = await browser.GetAsync("/api/auth/device/lookup?userCode=BCDFGHJK", TestContext.Current.CancellationToken);
            wrong.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using var locked = await browser.GetAsync($"/api/auth/device/lookup?userCode={pairing["userCode"]!.GetValue<string>()}", TestContext.Current.CancellationToken);
        locked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await TitleAsync(locked)).Should().Be("Too many wrong codes");
    }

    [Fact]
    public async Task A_pairing_can_be_answered_only_once()
    {
        using var host = Host();
        var user = await AddUserAsync(host);
        var pairing = await StartAsync(Tv(host));
        var browser = Browser(host, user);
        var body = new { userCode = pairing["userCode"]!.GetValue<string>() };

        using var first = await browser.PostAsJsonAsync("/api/auth/device/approve", body, TestContext.Current.CancellationToken);
        using var second = await browser.PostAsJsonAsync("/api/auth/device/approve", body, TestContext.Current.CancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task With_password_sign_in_off_a_password_account_cannot_approve_but_an_administrator_can()
    {
        using var host = Host();
        await TurnPasswordSignInOffAsync(host);
        var member = await AddUserAsync(host, "member@example.com");
        var admin = await AddUserAsync(host, "admin@example.com", isAdmin: true);
        var pairing = await StartAsync(Tv(host));
        var body = new { userCode = pairing["userCode"]!.GetValue<string>() };

        using var refused = await Browser(host, member).PostAsJsonAsync("/api/auth/device/approve", body, TestContext.Current.CancellationToken);
        using var allowed = await Browser(host, admin).PostAsJsonAsync("/api/auth/device/approve", body, TestContext.Current.CancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TitleAsync(refused)).Should().Be("Password sign-in is turned off");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_blocked_device_cannot_start_a_pairing()
    {
        using var host = Host();
        await BlockDeviceAsync(host, "tv-blocked");

        using var response = await Tv(host, "tv-blocked").PostAsync("/api/auth/device/start", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_device_blocked_after_it_asked_cannot_be_approved()
    {
        using var host = Host();
        var user = await AddUserAsync(host);
        var pairing = await StartAsync(Tv(host, "tv-late"));
        await BlockDeviceAsync(host, "tv-late");

        using var response = await Browser(host, user).PostAsJsonAsync("/api/auth/device/approve", new { userCode = pairing["userCode"]!.GetValue<string>() }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TitleAsync(response)).Should().Be("Device blocked");
    }

    [Fact]
    public async Task A_device_that_polls_too_fast_is_told_to_slow_down()
    {
        using var host = Host();
        var tv = Tv(host);
        var pairing = await StartAsync(tv);
        var deviceCode = pairing["deviceCode"]!.GetValue<string>();

        await PollAsync(tv, deviceCode);
        var hurried = await PollAsync(tv, deviceCode);

        hurried["status"]!.GetValue<string>().Should().Be("slow_down");
        hurried["intervalSeconds"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task An_unknown_device_code_has_expired()
    {
        using var host = Host();

        var polled = await PollAsync(Tv(host), "not-a-real-code");

        polled["status"]!.GetValue<string>().Should().Be("expired");
    }
}
