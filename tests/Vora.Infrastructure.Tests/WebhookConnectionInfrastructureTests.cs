using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Webhooks;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Vora.Infrastructure.Webhooks;

namespace Vora.Infrastructure.Tests;

public class WebhookConnectionInfrastructureTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>().UseInMemoryDatabase("webhook-connections-" + Guid.NewGuid().ToString("N")).Options);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task Connections_are_added_found_updated_and_deleted()
    {
        await using var db = NewContext();
        var repo = new WebhookConnectionRepository(db);
        var connection = new WebhookConnection { Name = "Radarr", ProviderId = "radarr_webhook", SecretCiphertext = "sealed", PathMappings = "/a => /b" };

        await repo.AddAsync(connection);
        db.ChangeTracker.Clear();
        (await repo.GetAllAsync()).Should().ContainSingle(c => c.Id == connection.Id);
        (await repo.GetAsync(connection.Id))!.PathMappings.Should().Be("/a => /b");

        var edited = (await repo.GetAsync(connection.Id))!;
        edited.Name = "Radarr 4K";
        edited.IsEnabled = false;
        await repo.UpdateAsync(edited);
        var reread = (await repo.GetAsync(connection.Id))!;
        reread.Name.Should().Be("Radarr 4K");
        reread.IsEnabled.Should().BeFalse();

        (await repo.DeleteAsync(connection.Id)).Should().BeTrue();
        (await repo.DeleteAsync(connection.Id)).Should().BeFalse();
        (await repo.GetAsync(connection.Id)).Should().BeNull();
    }

    [Fact]
    public void A_secret_is_sealed_and_opens_only_with_the_same_keys()
    {
        var sealer = new DataProtectionWebhookSecretProtector(new EphemeralDataProtectionProvider());
        var other = new DataProtectionWebhookSecretProtector(new EphemeralDataProtectionProvider());

        var ciphertext = sealer.Protect("my-secret");

        ciphertext.Should().NotContain("my-secret");
        sealer.Unprotect(ciphertext).Should().Be("my-secret");
        var open = () => other.Unprotect(ciphertext);
        open.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task The_root_folders_come_from_the_apps_v3_api_with_its_key()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[{\"id\":1,\"path\":\"/data/movies\"},{\"id\":2,\"path\":\"/data/4k\"},{\"id\":3},{\"path\":\"\"}]", Encoding.UTF8, "application/json")
        });
        var client = new ArrRootFolderClient(new HttpClient(handler));

        var folders = await client.GetRootFoldersAsync("http://radarr.lan:7878/radarr/", "the-key", TestContext.Current.CancellationToken);

        folders.Should().Equal("/data/movies", "/data/4k");
        handler.Requests.Single().RequestUri!.ToString().Should().Be("http://radarr.lan:7878/radarr/api/v3/rootfolder");
        handler.Requests.Single().Headers.GetValues("X-Api-Key").Should().Equal("the-key");
    }

    [Fact]
    public async Task An_error_or_an_answer_that_is_not_a_list_is_thrown_for_the_caller_to_report()
    {
        var refused = new ArrRootFolderClient(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        var notAList = new ArrRootFolderClient(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") })));

        var first = () => refused.GetRootFoldersAsync("http://x", "k", TestContext.Current.CancellationToken);
        var second = () => notAList.GetRootFoldersAsync("http://x", "k", TestContext.Current.CancellationToken);

        await first.Should().ThrowAsync<HttpRequestException>();
        await second.Should().ThrowAsync<InvalidOperationException>();
    }
}
