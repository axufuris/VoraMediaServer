using System.Net;
using Microsoft.AspNetCore.Hosting;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public sealed class WebClientFileCachingTests : IClassFixture<VoraApiTestFactory>, IDisposable
{
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "vora-test-webroot-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _client;

    public WebClientFileCachingTests(VoraApiTestFactory factory)
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>Vora</title>");
        File.WriteAllText(Path.Combine(_webRoot, "assets", "HomePage-3f9a1c.js"), "export default 1;");
        File.WriteAllText(Path.Combine(_webRoot, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        File.WriteAllText(Path.Combine(_webRoot, "sw.js"), "self.addEventListener('install', () => self.skipWaiting());");
        File.WriteAllText(Path.Combine(_webRoot, "manifest.webmanifest"), "{\"name\":\"Vora\"}");
        _client = factory.WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot)).CreateClient();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/server/7d1f/library/42")]
    [InlineData("/index.html")]
    public async Task The_app_shell_is_checked_with_the_server_every_time(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("<title>Vora</title>");
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl?.NoCache.Should().BeTrue();
        response.Headers.CacheControl?.MaxAge.Should().BeNull();
    }

    [Theory]
    [InlineData("/sw.js", "text/javascript")]
    [InlineData("/manifest.webmanifest", "application/manifest+json")]
    public async Task The_service_worker_and_the_app_manifest_are_checked_with_the_server_every_time(string path, string contentType)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be(contentType);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl?.NoCache.Should().BeTrue();
    }

    [Fact]
    public async Task A_hashed_page_file_is_kept_for_a_year_without_asking_again()
    {
        var response = await _client.GetAsync("/assets/HomePage-3f9a1c.js", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl?.Public.Should().BeTrue();
        response.Headers.CacheControl?.MaxAge.Should().Be(TimeSpan.FromDays(365));
        response.Headers.CacheControl?.Extensions.Select(extension => extension.Name).Should().Contain("immutable");
    }

    [Fact]
    public async Task A_file_without_a_hash_in_its_name_keeps_the_default_caching()
    {
        var response = await _client.GetAsync("/favicon.svg", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().BeNull();
    }

    [Fact]
    public async Task A_page_file_from_an_older_build_is_not_found_rather_than_answered_with_the_app_shell()
    {
        var response = await _client.GetAsync("/assets/HomePage-0ld0ld.js", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public void Dispose()
    {
        _client.Dispose();
        if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true);
    }
}
