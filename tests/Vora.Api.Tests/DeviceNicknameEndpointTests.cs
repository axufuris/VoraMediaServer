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

public class DeviceNicknameEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public DeviceNicknameEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<ClientDevice> SeedDeviceAsync(string? nickname = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var device = new ClientDevice
        {
            DeviceId = $"tv-{Guid.NewGuid():N}",
            DeviceName = "Shield",
            ClientName = "Vora Android",
            DeviceType = "TV",
            OperatingSystem = "Android",
            Nickname = nickname ?? string.Empty,
            LastConnectedAt = DateTime.UtcNow.AddHours(-3),
        };
        db.ClientDevices.Add(device);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return device;
    }

    private async Task<string> NicknameInDatabaseAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        return (await db.ClientDevices.AsNoTracking().SingleAsync(d => d.Id == id, TestContext.Current.CancellationToken)).Nickname;
    }

    private HttpClient Client(Guid accountId, bool isAdmin)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueAccountToken(accountId, isAdmin));
        return client;
    }

    private static Task<HttpResponseMessage> PutNicknameAsync(HttpClient client, Guid deviceId, string json) =>
        client.PutAsync($"/api/admin/devices/{deviceId}/nickname", new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    [Fact]
    public async Task An_admin_names_a_device_and_the_device_list_carries_the_nickname()
    {
        var device = await SeedDeviceAsync();
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        using var saved = await PutNicknameAsync(admin, device.Id, """{"nickname":"  Living   room "}""");

        saved.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await NicknameInDatabaseAsync(device.Id)).Should().Be("Living room");
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/devices", TestContext.Current.CancellationToken);
        var listed = list.EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == device.Id);
        listed.GetProperty("nickname").GetString().Should().Be("Living room");
        listed.GetProperty("deviceName").GetString().Should().Be("Shield");
    }

    [Theory]
    [InlineData("""{"nickname":""}""")]
    [InlineData("""{"nickname":"   "}""")]
    [InlineData("""{"nickname":null}""")]
    [InlineData("""{}""")]
    public async Task A_blank_or_missing_nickname_takes_the_nickname_off(string body)
    {
        var device = await SeedDeviceAsync("Living room");
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        using var saved = await PutNicknameAsync(admin, device.Id, body);

        saved.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await NicknameInDatabaseAsync(device.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_nickname_that_is_too_long_is_a_bad_request_and_the_old_one_stays()
    {
        var device = await SeedDeviceAsync("Living room");
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        using var saved = await PutNicknameAsync(admin, device.Id, $$"""{"nickname":"{{new string('x', 65)}}"}""");

        saved.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await NicknameInDatabaseAsync(device.Id)).Should().Be("Living room");
    }

    [Fact]
    public async Task A_device_that_is_not_there_is_not_found()
    {
        using var admin = Client(Guid.NewGuid(), isAdmin: true);

        using var saved = await PutNicknameAsync(admin, Guid.NewGuid(), """{"nickname":"Kitchen"}""");

        saved.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_an_admin_can_name_a_device()
    {
        var device = await SeedDeviceAsync();
        using var viewer = Client(Guid.NewGuid(), isAdmin: false);

        using var saved = await PutNicknameAsync(viewer, device.Id, """{"nickname":"Mine now"}""");

        saved.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await NicknameInDatabaseAsync(device.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_device_keeps_its_nickname_while_it_is_tracked_as_it_is_used()
    {
        var device = await SeedDeviceAsync("Living room");
        var account = Guid.NewGuid();
        using var client = Client(account, isAdmin: false);
        client.DefaultRequestHeaders.Add("X-Vora-Device-Id", device.DeviceId);
        client.DefaultRequestHeaders.Add("X-Vora-Device", "Shield TV Pro");

        using var response = await client.GetAsync("/api/users/profiles/me", TestContext.Current.CancellationToken);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var tracked = await db.ClientDevices.AsNoTracking().SingleAsync(d => d.Id == device.Id, TestContext.Current.CancellationToken);
        tracked.LastUserId.Should().Be(account, $"the request ({(int)response.StatusCode}) went through device tracking");
        tracked.DeviceName.Should().Be("Shield TV Pro");
        tracked.Nickname.Should().Be("Living room");
    }
}
