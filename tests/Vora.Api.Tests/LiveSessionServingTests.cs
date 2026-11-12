using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vora.Api.Tests.Infra;
using Vora.Application.Iptv;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Domain.Entities.Settings;

namespace Vora.Api.Tests;

public sealed class LiveSessionServingTests : IClassFixture<VoraApiTestFactory>, IDisposable
{
    private readonly VoraApiTestFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-live-serving-" + Guid.NewGuid().ToString("N"));

    public LiveSessionServingTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Fetching_a_live_remux_keeps_it_from_being_stopped_as_idle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewer = Guid.NewGuid();
        const string session = "0123456789abcdef0123456789abcdef";
        var folder = Path.Combine(_root, TimeshiftCoordinator.TimeshiftSubdirectory, viewer.ToString(), session);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "index.m3u8"), "#EXTM3U\n", cancellationToken);
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { TranscoderTempDirectory = _root });
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISystemSettingsRepository>();
            services.AddSingleton(settings);
        }));
        var coordinator = factory.Services.GetRequiredService<ITimeshiftCoordinator>();
        coordinator.TryRegister(viewer, new Process(), folder, LiveSessionKind.Remux).Should().BeTrue();
        var token = factory.Services.GetRequiredService<IStreamingTokenSigner>().Sign("timeshift", $"{viewer}:{session}", TimeSpan.FromMinutes(5));
        await Task.Delay(400, cancellationToken);

        var response = await factory.CreateClient().GetAsync($"/api/streaming/hls/timeshift/{token}/{viewer}/{session}/index.m3u8", cancellationToken);
        var evicted = await coordinator.EvictStaleSessionsAsync(TimeSpan.FromHours(1), TimeSpan.FromHours(6), remuxMaxIdle: TimeSpan.FromMilliseconds(300));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        evicted.Should().BeEmpty();
        coordinator.KindOf(viewer).Should().Be(LiveSessionKind.Remux);
        await coordinator.StopAsync(viewer);
    }
}
