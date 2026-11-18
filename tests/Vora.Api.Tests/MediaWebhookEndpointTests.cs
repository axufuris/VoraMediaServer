using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

    private async Task<string> NewSecretAsync(string providerId)
    {
        using var admin = AdminClient();
        using var response = await admin.PostAsync($"/api/admin/webhooks/{providerId}/secret", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<MediaWebhookSecretVM>(TestContext.Current.CancellationToken))!.Secret;
    }

    private static HttpRequestMessage Post(string providerId, string body, string? basicPassword = null, string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/{providerId}{query}")
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
    public async Task A_provider_with_no_secret_is_not_there()
    {
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Post("lidarr_webhook", TestPayload, "anything"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_provider_is_not_there()
    {
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Post("nope_webhook", TestPayload, "anything"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_secret_works_as_a_basic_auth_password_and_a_wrong_one_is_refused()
    {
        var secret = await NewSecretAsync("radarr_webhook");
        using var client = _factory.CreateClient();

        using var ok = await client.SendAsync(Post("radarr_webhook", TestPayload, secret), TestContext.Current.CancellationToken);
        using var wrong = await client.SendAsync(Post("radarr_webhook", TestPayload, secret + "x"), TestContext.Current.CancellationToken);
        using var none = await client.SendAsync(Post("radarr_webhook", TestPayload), TestContext.Current.CancellationToken);

        ok.StatusCode.Should().Be(HttpStatusCode.Accepted);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_secret_also_works_as_a_header_or_a_query_value_and_put_is_accepted()
    {
        var secret = await NewSecretAsync("sonarr_webhook");
        using var client = _factory.CreateClient();

        var byHeader = Post("sonarr_webhook", TestPayload);
        byHeader.Headers.Add("X-Api-Key", secret);
        using var header = await client.SendAsync(byHeader, TestContext.Current.CancellationToken);
        using var query = await client.SendAsync(Post("sonarr_webhook", TestPayload, query: "?token=" + Uri.EscapeDataString(secret)), TestContext.Current.CancellationToken);
        var put = Post("sonarr_webhook", TestPayload, secret);
        put.Method = HttpMethod.Put;
        using var putResponse = await client.SendAsync(put, TestContext.Current.CancellationToken);

        header.StatusCode.Should().Be(HttpStatusCode.Accepted);
        query.StatusCode.Should().Be(HttpStatusCode.Accepted);
        putResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_bad_request()
    {
        var secret = await NewSecretAsync("radarr_webhook");
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Post("radarr_webhook", "<html>", secret), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_body_over_a_megabyte_is_refused()
    {
        var secret = await NewSecretAsync("radarr_webhook");
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(Post("radarr_webhook", new string('x', 1_100_000), secret), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task The_admin_endpoints_are_for_admins_only()
    {
        using var viewer = _factory.CreateClient();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false));
        using var anonymous = _factory.CreateClient();

        using var list = await viewer.GetAsync("/api/admin/webhooks", TestContext.Current.CancellationToken);
        using var deliveries = await viewer.GetAsync("/api/admin/webhooks/deliveries", TestContext.Current.CancellationToken);
        using var secret = await viewer.PostAsync("/api/admin/webhooks/radarr_webhook/secret", null, TestContext.Current.CancellationToken);
        using var none = await anonymous.GetAsync("/api/admin/webhooks", TestContext.Current.CancellationToken);

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deliveries.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        secret.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_admin_sees_the_three_providers_and_their_deliveries_and_never_a_secret()
    {
        var secret = await NewSecretAsync("radarr_webhook");
        using var client = _factory.CreateClient();
        using var delivered = await client.SendAsync(Post("radarr_webhook", TestPayload, secret), TestContext.Current.CancellationToken);
        using var admin = AdminClient();

        var providers = await admin.GetFromJsonAsync<List<MediaWebhookProviderVM>>("/api/admin/webhooks", TestContext.Current.CancellationToken);
        var deliveries = await admin.GetFromJsonAsync<List<MediaWebhookDelivery>>("/api/admin/webhooks/deliveries", TestContext.Current.CancellationToken);
        var listText = await admin.GetStringAsync("/api/admin/webhooks", TestContext.Current.CancellationToken);

        providers!.Select(p => p.Id).Should().Equal("lidarr_webhook", "radarr_webhook", "sonarr_webhook");
        providers.Single(p => p.Id == "radarr_webhook").IsConfigured.Should().BeTrue();
        providers.Single(p => p.Id == "radarr_webhook").ReceivePath.Should().Be("/api/webhooks/radarr_webhook");
        deliveries!.Should().Contain(d => d.ProviderId == "radarr_webhook" && d.Event == "Test");
        listText.Should().NotContain(secret);
    }
}
