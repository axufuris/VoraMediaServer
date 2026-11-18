using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Settings;
using Vora.Application.Webhooks;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Providers.Radarr;
using Vora.Plugins.Providers.Sonarr;

namespace Vora.Application.Tests.Webhooks;

public class MediaWebhookManagerTests
{
    private const string Secret = "s3cret-value-for-tests";

    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IMediaWebhookQueue _queue = Substitute.For<IMediaWebhookQueue>();
    private readonly MediaWebhookLog _log = new();
    private readonly MediaWebhookManager _manager;

    private const string Import = "{\"eventType\":\"Download\",\"instanceName\":\"Radarr\",\"movieFile\":{\"path\":\"/movies/A (2020)/A.mkv\"}}";

    public MediaWebhookManagerTests()
    {
        _settings.GetPluginSettingAsync("radarr_webhook", MediaWebhookSettingKeys.Secret).Returns(Secret);
        _manager = new MediaWebhookManager(new Vora.Plugins.Interfaces.IMediaWebhookProvider[] { new RadarrWebhookProvider(), new SonarrWebhookProvider() }, _settings, _queue, _log, NullLogger<MediaWebhookManager>.Instance);
    }

    [Fact]
    public async Task A_correct_secret_queues_what_the_payload_changed()
    {
        var receipt = await _manager.ReceiveAsync("radarr_webhook", Import, new[] { Secret });

        receipt.Status.Should().Be(MediaWebhookStatus.Accepted);
        _queue.Received(1).Enqueue(Arg.Is<MediaWebhookWork>(w =>
            w.ProviderId == "radarr_webhook" && w.InstanceName == "Radarr" && w.EventName == "Download" && w.LibraryType == LibraryType.Movie
            && w.Changes.Count == 1 && w.Changes[0].Path == "/movies/A (2020)/A.mkv" && w.Changes[0].Kind == MediaWebhookChangeKind.Added));
    }

    [Fact]
    public async Task Any_one_of_the_presented_secrets_may_be_the_right_one()
    {
        var receipt = await _manager.ReceiveAsync("radarr_webhook", Import, new[] { "vora", Secret });

        receipt.Status.Should().Be(MediaWebhookStatus.Accepted);
    }

    [Fact]
    public async Task A_wrong_or_missing_secret_is_refused_and_nothing_is_queued()
    {
        (await _manager.ReceiveAsync("radarr_webhook", Import, new[] { "wrong" })).Status.Should().Be(MediaWebhookStatus.Unauthorized);
        (await _manager.ReceiveAsync("radarr_webhook", Import, Array.Empty<string>())).Status.Should().Be(MediaWebhookStatus.Unauthorized);
        (await _manager.ReceiveAsync("radarr_webhook", Import, new[] { Secret + "x" })).Status.Should().Be(MediaWebhookStatus.Unauthorized);

        _queue.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_provider_with_no_secret_is_not_listening_at_all()
    {
        var receipt = await _manager.ReceiveAsync("sonarr_webhook", Import, new[] { Secret });

        receipt.Status.Should().Be(MediaWebhookStatus.NotFound);
    }

    [Fact]
    public async Task A_provider_that_is_switched_off_is_not_listening_even_with_the_right_secret()
    {
        _settings.GetPluginSettingAsync("radarr_webhook", "is_enabled").Returns("false");

        var receipt = await _manager.ReceiveAsync("radarr_webhook", Import, new[] { Secret });

        receipt.Status.Should().Be(MediaWebhookStatus.NotFound);
        _queue.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_provider_is_not_found()
    {
        (await _manager.ReceiveAsync("plex_webhook", Import, new[] { Secret })).Status.Should().Be(MediaWebhookStatus.NotFound);
    }

    [Fact]
    public async Task A_test_is_answered_and_shown_in_the_log_without_queueing_anything()
    {
        var receipt = await _manager.ReceiveAsync("radarr_webhook", "{\"eventType\":\"Test\",\"instanceName\":\"Radarr 4K\"}", new[] { Secret });

        receipt.Status.Should().Be(MediaWebhookStatus.Accepted);
        receipt.Message.Should().Contain("listening");
        _queue.ReceivedCalls().Should().BeEmpty();
        _manager.GetDeliveries(10).Single().Should().Match<MediaWebhookDelivery>(d => d.Event == "Test" && d.Instance == "Radarr 4K" && d.Ok);
    }

    [Fact]
    public async Task An_event_that_changes_no_files_is_accepted_so_the_app_does_not_flag_a_failure()
    {
        var receipt = await _manager.ReceiveAsync("radarr_webhook", "{\"eventType\":\"Grab\"}", new[] { Secret });

        receipt.Status.Should().Be(MediaWebhookStatus.Accepted);
        _queue.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_bad_request()
    {
        (await _manager.ReceiveAsync("radarr_webhook", "<html>", new[] { Secret })).Status.Should().Be(MediaWebhookStatus.BadRequest);
    }

    [Fact]
    public async Task Generating_a_secret_stores_a_long_random_one_and_returns_it_once()
    {
        var first = await _manager.GenerateSecretAsync("sonarr_webhook");
        var second = await _manager.GenerateSecretAsync("sonarr_webhook");

        first!.Secret.Length.Should().BeGreaterThanOrEqualTo(40);
        first.Secret.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        second!.Secret.Should().NotBe(first.Secret);
        await _settings.Received(1).SetPluginSettingAsync("sonarr_webhook", MediaWebhookSettingKeys.Secret, first.Secret);
        (await _manager.GenerateSecretAsync("plex_webhook")).Should().BeNull();
    }

    [Fact]
    public async Task The_provider_list_says_where_to_post_and_whether_each_is_set_up_without_revealing_a_secret()
    {
        var providers = await _manager.GetProvidersAsync();

        providers.Select(p => p.Id).Should().Equal("radarr_webhook", "sonarr_webhook");
        var radarr = providers.Single(p => p.Id == "radarr_webhook");
        radarr.ReceivePath.Should().Be("/api/webhooks/radarr_webhook");
        radarr.IsConfigured.Should().BeTrue();
        radarr.IsEnabled.Should().BeTrue();
        radarr.LibraryKind.Should().Be("Movie");
        providers.Single(p => p.Id == "sonarr_webhook").IsConfigured.Should().BeFalse();
        System.Text.Json.JsonSerializer.Serialize(providers).Should().NotContain(Secret);
    }
}
