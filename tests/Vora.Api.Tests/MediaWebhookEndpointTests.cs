using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Vora.Api.Tests.Infra;
using Vora.Application.Webhooks;

namespace Vora.Api.Tests;

public class MediaWebhookEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private const string TestPayload = "{\"eventType\":\"Test\",\"instanceName\":\"Radarr\"}";

    private readonly VoraApiTestFactory _factory;

    public MediaWebhookEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(Guid Id, string Secret)> NewConnectionAsync(string provider = "radarr_webhook", string name = "Radarr", object? settings = null)
    {
        using var admin = AdminClient();
        using var response = await admin.PostAsJsonAsync("/api/admin/webhooks/connections", new { name, providerId = provider, settings }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<WebhookConnectionVM>(TestContext.Current.CancellationToken))!;
        return (body.Id, body.Secret!);
    }

    private static HttpRequestMessage Post(Guid id, string body, string? basicPassword = null, string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/{id}{query}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (basicPassword != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("vora:" + basicPassword)));
        }
        return request;
    }

    [Fact]
    public async Task An_unknown_connection_or_one_that_is_not_an_id_is_not_there()
    {
        using var client = _factory.CreateClient();

        using var unknown = await client.SendAsync(Post(Guid.NewGuid(), TestPayload, "anything"), TestContext.Current.CancellationToken);
        using var old = await client.PostAsync("/api/webhooks/radarr_webhook", new StringContent(TestPayload, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        old.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_secret_works_as_a_basic_auth_password_and_a_wrong_one_is_refused()
    {
        var (id, secret) = await NewConnectionAsync();
        using var client = _factory.CreateClient();

        using var ok = await client.SendAsync(Post(id, TestPayload, secret), TestContext.Current.CancellationToken);
        using var wrong = await client.SendAsync(Post(id, TestPayload, secret + "x"), TestContext.Current.CancellationToken);
        using var none = await client.SendAsync(Post(id, TestPayload), TestContext.Current.CancellationToken);

        ok.StatusCode.Should().Be(HttpStatusCode.Accepted);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_secret_also_works_as_a_header_or_a_query_value_and_put_is_accepted()
    {
        var (id, secret) = await NewConnectionAsync("sonarr_webhook", "Sonarr");
        using var client = _factory.CreateClient();

        var byHeader = Post(id, TestPayload);
        byHeader.Headers.Add("X-Api-Key", secret);
        using var header = await client.SendAsync(byHeader, TestContext.Current.CancellationToken);
        using var query = await client.SendAsync(Post(id, TestPayload, query: "?token=" + Uri.EscapeDataString(secret)), TestContext.Current.CancellationToken);
        var put = Post(id, TestPayload, secret);
        put.Method = HttpMethod.Put;
        using var putResponse = await client.SendAsync(put, TestContext.Current.CancellationToken);

        header.StatusCode.Should().Be(HttpStatusCode.Accepted);
        query.StatusCode.Should().Be(HttpStatusCode.Accepted);
        putResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_bad_request_and_one_over_a_megabyte_is_refused()
    {
        var (id, secret) = await NewConnectionAsync();
        using var client = _factory.CreateClient();

        using var bad = await client.SendAsync(Post(id, "<html>", secret), TestContext.Current.CancellationToken);
        using var large = await client.SendAsync(Post(id, new string('x', 1_100_000), secret), TestContext.Current.CancellationToken);

        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        large.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_custom_connection_takes_a_script_s_simple_post()
    {
        var (id, secret) = await NewConnectionAsync("custom_webhook", "Script", new { library_type = "Movie" });
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Post(id, "{\"event\":\"test\"}", secret), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Two_connections_of_one_template_have_their_own_address_and_secret()
    {
        var (first, firstSecret) = await NewConnectionAsync(name: "Radarr HD");
        var (second, secondSecret) = await NewConnectionAsync(name: "Radarr 4K");
        using var client = _factory.CreateClient();

        using var own = await client.SendAsync(Post(first, TestPayload, firstSecret), TestContext.Current.CancellationToken);
        using var crossed = await client.SendAsync(Post(first, TestPayload, secondSecret), TestContext.Current.CancellationToken);

        first.Should().NotBe(second);
        own.StatusCode.Should().Be(HttpStatusCode.Accepted);
        crossed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Switching_a_connection_off_or_deleting_it_makes_it_stop_answering_and_a_new_secret_retires_the_old()
    {
        var (id, secret) = await NewConnectionAsync(name: "Radarr to change");
        using var admin = AdminClient();
        using var client = _factory.CreateClient();

        using var regenerated = await admin.PostAsync($"/api/admin/webhooks/connections/{id}/secret", null, TestContext.Current.CancellationToken);
        var fresh = (await regenerated.Content.ReadFromJsonAsync<MediaWebhookSecretVM>(TestContext.Current.CancellationToken))!.Secret;
        using var old = await client.SendAsync(Post(id, TestPayload, secret), TestContext.Current.CancellationToken);
        using var current = await client.SendAsync(Post(id, TestPayload, fresh), TestContext.Current.CancellationToken);

        using var off = await admin.PutAsJsonAsync($"/api/admin/webhooks/connections/{id}", new { name = "Radarr to change", isEnabled = false, handleDeletes = true, pathMappings = "" }, TestContext.Current.CancellationToken);
        using var whileOff = await client.SendAsync(Post(id, TestPayload, fresh), TestContext.Current.CancellationToken);

        using var deleted = await admin.DeleteAsync($"/api/admin/webhooks/connections/{id}", TestContext.Current.CancellationToken);
        using var again = await admin.DeleteAsync($"/api/admin/webhooks/connections/{id}", TestContext.Current.CancellationToken);

        old.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        current.StatusCode.Should().Be(HttpStatusCode.Accepted);
        off.StatusCode.Should().Be(HttpStatusCode.OK);
        whileOff.StatusCode.Should().Be(HttpStatusCode.NotFound);
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_connection_with_a_bad_field_is_a_bad_request_that_says_why()
    {
        using var admin = AdminClient();

        using var noName = await admin.PostAsJsonAsync("/api/admin/webhooks/connections", new { name = " ", providerId = "radarr_webhook" }, TestContext.Current.CancellationToken);
        using var noTemplate = await admin.PostAsJsonAsync("/api/admin/webhooks/connections", new { name = "X", providerId = "nope_webhook" }, TestContext.Current.CancellationToken);
        using var missing = await admin.PutAsJsonAsync($"/api/admin/webhooks/connections/{Guid.NewGuid()}", new { name = "X", isEnabled = true, handleDeletes = true }, TestContext.Current.CancellationToken);

        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noName.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("name");
        noTemplate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_admin_endpoints_are_for_admins_only()
    {
        using var viewer = _factory.CreateClient();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false));
        using var anonymous = _factory.CreateClient();
        var id = Guid.NewGuid();

        foreach (var path in new[] { "providers", "connections", $"connections/{id}", "deliveries", "request-servers", $"request-servers/{id}/mappings" })
        {
            using var asViewer = await viewer.GetAsync($"/api/admin/webhooks/{path}", TestContext.Current.CancellationToken);
            using var asAnonymous = await anonymous.GetAsync($"/api/admin/webhooks/{path}", TestContext.Current.CancellationToken);
            asViewer.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
            asAnonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
        }

        using var create = await viewer.PostAsJsonAsync("/api/admin/webhooks/connections", new { name = "X", providerId = "radarr_webhook" }, TestContext.Current.CancellationToken);
        using var preview = await viewer.PostAsJsonAsync("/api/admin/webhooks/preview", new { providerId = "radarr_webhook", body = "{}" }, TestContext.Current.CancellationToken);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        preview.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_sees_the_templates_the_connections_without_secrets_and_each_one_s_deliveries()
    {
        var (id, secret) = await NewConnectionAsync(name: "Radarr listed");
        using var client = _factory.CreateClient();
        using var delivered = await client.SendAsync(Post(id, TestPayload, secret), TestContext.Current.CancellationToken);
        using var admin = AdminClient();

        var providers = await admin.GetFromJsonAsync<List<WebhookProviderVM>>("/api/admin/webhooks/providers", TestContext.Current.CancellationToken);
        var listText = await admin.GetStringAsync("/api/admin/webhooks/connections", TestContext.Current.CancellationToken);
        var detail = await admin.GetFromJsonAsync<WebhookConnectionVM>($"/api/admin/webhooks/connections/{id}", TestContext.Current.CancellationToken);
        var deliveries = await admin.GetFromJsonAsync<List<MediaWebhookDelivery>>($"/api/admin/webhooks/deliveries?connectionId={id}", TestContext.Current.CancellationToken);
        var others = await admin.GetFromJsonAsync<List<MediaWebhookDelivery>>($"/api/admin/webhooks/deliveries?connectionId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        providers!.Select(p => p.Id).Should().Equal("lidarr_webhook", "radarr_webhook", "sonarr_webhook", "custom_webhook");
        listText.Should().Contain("Radarr listed").And.NotContain(secret);
        detail!.Secret.Should().Be(secret);
        detail.ReceivePath.Should().Be($"/api/webhooks/{id}");
        deliveries!.Should().ContainSingle(d => d.ConnectionName == "Radarr listed" && d.Event == "Test");
        others.Should().BeEmpty();
    }

    [Fact]
    public async Task A_sample_payload_can_be_tried_before_anything_is_saved()
    {
        using var admin = AdminClient();

        using var response = await admin.PostAsJsonAsync("/api/admin/webhooks/preview", new
        {
            providerId = "custom_webhook",
            body = "{\"event\":\"added\",\"path\":\"/nowhere/x.mkv\"}",
            settings = new { library_type = "Movie" },
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        preview.GetProperty("ok").GetBoolean().Should().BeTrue();
        preview.GetProperty("event").GetString().Should().Be("added");
        preview.GetProperty("changes")[0].GetProperty("kind").GetString().Should().Be("Added");
        preview.GetProperty("changes")[0].GetProperty("reportedPath").GetString().Should().Be("/nowhere/x.mkv");
    }

    [Fact]
    public async Task Request_servers_for_filling_in_start_empty_and_an_unknown_one_is_not_found()
    {
        using var admin = AdminClient();

        var servers = await admin.GetFromJsonAsync<List<WebhookRequestServerVM>>("/api/admin/webhooks/request-servers", TestContext.Current.CancellationToken);
        using var missing = await admin.GetAsync($"/api/admin/webhooks/request-servers/{Guid.NewGuid()}/mappings", TestContext.Current.CancellationToken);

        servers.Should().BeEmpty();
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
