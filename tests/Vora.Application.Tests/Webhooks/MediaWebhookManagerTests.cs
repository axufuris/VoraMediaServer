using System.Linq.Expressions;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Requests;
using Vora.Application.Requests.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Webhooks;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Webhooks;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Custom;
using Vora.Plugins.Providers.Lidarr;
using Vora.Plugins.Providers.Radarr;
using Vora.Plugins.Providers.Sonarr;

namespace Vora.Application.Tests.Webhooks;

public sealed class MediaWebhookManagerTests : IDisposable
{
    private sealed class FakeConnections : IWebhookConnectionRepository
    {
        public List<WebhookConnection> Rows { get; } = new();

        public Task<List<WebhookConnection>> GetAllAsync() => Task.FromResult(Rows.ToList());

        public Task<WebhookConnection?> GetAsync(Guid id) => Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));

        public Task AddAsync(WebhookConnection connection)
        {
            Rows.Add(connection);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(WebhookConnection connection) => Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid id) => Task.FromResult(Rows.RemoveAll(r => r.Id == id) > 0);
    }

    private sealed class FakeProtector : IWebhookSecretProtector
    {
        public string Protect(string plaintext) => "sealed:" + plaintext;

        public string Unprotect(string ciphertext) =>
            ciphertext.StartsWith("sealed:", StringComparison.Ordinal) ? ciphertext["sealed:".Length..] : throw new CryptographicException("Wrong keys.");
    }

    private readonly string _root = Directory.CreateTempSubdirectory("vora-manager-").FullName;
    private readonly FakeConnections _connections = new();
    private readonly IMediaWebhookQueue _queue = Substitute.For<IMediaWebhookQueue>();
    private readonly MediaWebhookLog _log = new();
    private readonly ILibraryRepository _libraries = Substitute.For<ILibraryRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IRequestRepository _requests = Substitute.For<IRequestRepository>();
    private readonly IArrRootFolderClient _arr = Substitute.For<IArrRootFolderClient>();
    private readonly Guid _tvLibrary = Guid.NewGuid();
    private readonly string _movies;
    private readonly MediaWebhookManager _manager;

    private const string Import = "{\"eventType\":\"Download\",\"instanceName\":\"Radarr\",\"movieFile\":{\"path\":\"/movies/A (2020)/A.mkv\"}}";

    public MediaWebhookManagerTests()
    {
        _movies = Directory.CreateDirectory(Path.Combine(_root, "movies")).FullName;
        _settings.GetSettingsAsync().Returns(new ServerSetting());
        _libraries.GetAllProjectedAsync(Arg.Any<Expression<Func<MediaLibrary, MediaWebhookLibrary>>>(), Arg.Any<bool>(), Arg.Any<List<Guid>?>())
            .Returns(new[]
            {
                new MediaWebhookLibrary(Guid.NewGuid(), "Movies", LibraryType.Movie, new List<string> { _movies }, new List<string>()),
                new MediaWebhookLibrary(_tvLibrary, "TV", LibraryType.TvShow, new List<string> { Path.Combine(_root, "tv") }, new List<string>()),
            }.AsEnumerable());
        _libraries.GetProjectedByIdAsync(_tvLibrary, Arg.Any<Expression<Func<MediaLibrary, string>>>()).Returns("TV");

        _manager = new MediaWebhookManager(
            new IMediaWebhookProvider[] { new RadarrWebhookProvider(), new SonarrWebhookProvider(), new LidarrWebhookProvider(), new CustomWebhookProvider() },
            _connections, new FakeProtector(), _queue, _log, _libraries, _settings, _media, _requests, _arr, NullLogger<MediaWebhookManager>.Instance);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static SaveWebhookConnectionRequest Save(string provider, string name = "Radarr", Dictionary<string, string>? settings = null, string mappings = "") =>
        new() { Name = name, ProviderId = provider, PathMappings = mappings, Settings = settings };

    private async Task<(WebhookConnectionVM Connection, string Secret)> CreateAsync(string provider = "radarr_webhook", string name = "Radarr", Dictionary<string, string>? settings = null, string mappings = "")
    {
        var result = await _manager.CreateAsync(Save(provider, name, settings, mappings));
        result.Succeeded.Should().BeTrue(result.Error);
        return (result.Value!, result.Value!.Secret!);
    }

    [Fact]
    public async Task A_correct_secret_queues_what_the_payload_changed_with_the_connection_s_own_settings()
    {
        var (connection, secret) = await CreateAsync(mappings: "/data => /movies");

        var receipt = await _manager.ReceiveAsync(connection.Id.ToString(), Import, new[] { secret });

        receipt.Status.Should().Be(MediaWebhookStatus.Accepted);
        _queue.Received(1).Enqueue(Arg.Is<MediaWebhookWork>(w =>
            w.ConnectionId == connection.Id && w.ConnectionName == "Radarr" && w.ProviderId == "radarr_webhook" && w.InstanceName == "Radarr"
            && w.EventName == "Download" && w.LibraryType == LibraryType.Movie && w.PathMappings == "/data => /movies" && w.HandleDeletes
            && w.Changes.Count == 1 && w.Changes[0].Path == "/movies/A (2020)/A.mkv" && w.Changes[0].Kind == MediaWebhookChangeKind.Added));
    }

    [Fact]
    public async Task Any_one_of_the_presented_secrets_may_be_the_right_one()
    {
        var (connection, secret) = await CreateAsync();

        (await _manager.ReceiveAsync(connection.Id.ToString(), Import, new[] { "vora", secret })).Status.Should().Be(MediaWebhookStatus.Accepted);
    }

    [Fact]
    public async Task A_wrong_or_missing_secret_is_refused_and_nothing_is_queued()
    {
        var (connection, secret) = await CreateAsync();

        (await _manager.ReceiveAsync(connection.Id.ToString(), Import, new[] { "wrong" })).Status.Should().Be(MediaWebhookStatus.Unauthorized);
        (await _manager.ReceiveAsync(connection.Id.ToString(), Import, Array.Empty<string>())).Status.Should().Be(MediaWebhookStatus.Unauthorized);
        (await _manager.ReceiveAsync(connection.Id.ToString(), Import, new[] { secret + "x" })).Status.Should().Be(MediaWebhookStatus.Unauthorized);

        _queue.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_answers_for_an_unknown_switched_off_or_unreadable_connection()
    {
        var (connection, secret) = await CreateAsync();
        var switchedOff = await _manager.UpdateAsync(connection.Id, new SaveWebhookConnectionRequest { Name = "Radarr", ProviderId = "radarr_webhook", IsEnabled = false });
        switchedOff.Succeeded.Should().BeTrue();
        var (other, otherSecret) = await CreateAsync(name: "Other");
        _connections.Rows.Single(r => r.Id == other.Id).SecretCiphertext = "sealed with other keys";
        var (orphan, orphanSecret) = await CreateAsync(name: "Orphan");
        _connections.Rows.Single(r => r.Id == orphan.Id).ProviderId = "gone_webhook";

        (await _manager.ReceiveAsync(connection.Id.ToString(), Import, new[] { secret })).Status.Should().Be(MediaWebhookStatus.NotFound);
        (await _manager.ReceiveAsync(other.Id.ToString(), Import, new[] { otherSecret })).Status.Should().Be(MediaWebhookStatus.NotFound);
        (await _manager.ReceiveAsync(orphan.Id.ToString(), Import, new[] { orphanSecret })).Status.Should().Be(MediaWebhookStatus.NotFound);
        (await _manager.ReceiveAsync(Guid.NewGuid().ToString(), Import, new[] { secret })).Status.Should().Be(MediaWebhookStatus.NotFound);
        (await _manager.ReceiveAsync("radarr_webhook", Import, new[] { secret })).Status.Should().Be(MediaWebhookStatus.NotFound);
        _queue.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_test_is_answered_and_shown_against_its_connection_without_queueing_anything()
    {
        var (connection, secret) = await CreateAsync(name: "Radarr 4K");

        var receipt = await _manager.ReceiveAsync(connection.Id.ToString(), "{\"eventType\":\"Test\",\"instanceName\":\"Radarr 4K\"}", new[] { secret });

        receipt.Status.Should().Be(MediaWebhookStatus.Accepted);
        receipt.Message.Should().Contain("listening");
        _queue.ReceivedCalls().Should().BeEmpty();
        _manager.GetDeliveries(10, connection.Id).Single().Should().Match<MediaWebhookDelivery>(d => d.Event == "Test" && d.ConnectionName == "Radarr 4K" && d.Ok);
        _manager.GetDeliveries(10, Guid.NewGuid()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_that_changes_no_files_is_accepted_so_the_app_does_not_flag_a_failure()
    {
        var (connection, secret) = await CreateAsync();

        (await _manager.ReceiveAsync(connection.Id.ToString(), "{\"eventType\":\"Grab\"}", new[] { secret })).Status.Should().Be(MediaWebhookStatus.Accepted);
        _queue.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_bad_request()
    {
        var (connection, secret) = await CreateAsync();

        (await _manager.ReceiveAsync(connection.Id.ToString(), "<html>", new[] { secret })).Status.Should().Be(MediaWebhookStatus.BadRequest);
    }

    [Fact]
    public async Task A_custom_scan_connection_queues_a_scan_with_no_kind_of_library()
    {
        var (connection, secret) = await CreateAsync("custom_webhook", "Tdarr", new Dictionary<string, string>
        {
            [MediaWebhookSettingKeys.Action] = MediaWebhookSettingKeys.ScanAction,
            [MediaWebhookSettingKeys.ScanLibrary] = _tvLibrary.ToString(),
        });

        var receipt = await _manager.ReceiveAsync(connection.Id.ToString(), "{\"event\":\"added\"}", new[] { secret });

        receipt.Status.Should().Be(MediaWebhookStatus.Accepted);
        _queue.Received(1).Enqueue(Arg.Is<MediaWebhookWork>(w =>
            w.LibraryType == null && w.Changes.Single().Kind == MediaWebhookChangeKind.ScanLibrary && w.Settings[MediaWebhookSettingKeys.ScanLibrary] == _tvLibrary.ToString()));
    }

    [Fact]
    public async Task Creating_a_connection_makes_its_secret_stores_only_the_sealed_copy_and_applies_the_template_defaults()
    {
        var (connection, secret) = await CreateAsync("custom_webhook", "  Script  ");

        secret.Length.Should().BeGreaterThanOrEqualTo(40);
        secret.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        connection.Name.Should().Be("Script");
        connection.ReceivePath.Should().Be($"/api/webhooks/{connection.Id}");
        connection.IsEnabled.Should().BeTrue();
        connection.HandleDeletes.Should().BeTrue();
        connection.Settings[MediaWebhookSettingKeys.Action].Should().Be(MediaWebhookSettingKeys.IngestAction);
        connection.Settings[MediaWebhookSettingKeys.FilePath].Should().Be("path");
        _connections.Rows.Single().SecretCiphertext.Should().Be("sealed:" + secret);
        (await _manager.GetConnectionsAsync()).Single().Secret.Should().BeNull();
        (await _manager.GetConnectionAsync(connection.Id))!.Secret.Should().Be(secret);
    }

    [Fact]
    public async Task A_connection_is_refused_for_a_missing_name_template_or_required_setting()
    {
        (await _manager.CreateAsync(Save("radarr_webhook", "  "))).Error.Should().Contain("name");
        (await _manager.CreateAsync(Save("radarr_webhook", new string('x', 129)))).Error.Should().Contain("128");
        (await _manager.CreateAsync(Save("nope_webhook"))).Error.Should().Contain("template");
        (await _manager.CreateAsync(Save("radarr_webhook", mappings: "/tv /media/tv"))).Error.Should().Contain("/tv /media/tv").And.Contain("=>");
        (await _manager.CreateAsync(Save("custom_webhook", settings: new Dictionary<string, string> { [MediaWebhookSettingKeys.Action] = MediaWebhookSettingKeys.ScanAction })))
            .Error.Should().Contain("Library to scan is required");
        (await _manager.CreateAsync(Save("custom_webhook", settings: new Dictionary<string, string> { [MediaWebhookSettingKeys.LibraryType] = "Photos" })))
            .Error.Should().Contain("not one of the choices");
        (await _manager.CreateAsync(Save("custom_webhook", settings: new Dictionary<string, string>
        {
            [MediaWebhookSettingKeys.Action] = MediaWebhookSettingKeys.ScanAction,
            [MediaWebhookSettingKeys.ScanLibrary] = "not a guid",
        }))).Error.Should().Contain("not a library");
        _connections.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Settings_that_are_hidden_by_the_chosen_action_are_not_checked()
    {
        var result = await _manager.CreateAsync(Save("custom_webhook", settings: new Dictionary<string, string>
        {
            [MediaWebhookSettingKeys.Action] = MediaWebhookSettingKeys.ScanAction,
            [MediaWebhookSettingKeys.ScanLibrary] = _tvLibrary.ToString(),
            [MediaWebhookSettingKeys.LibraryType] = "Photos",
            [MediaWebhookSettingKeys.FilePath] = string.Empty,
        }));

        result.Succeeded.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task Updating_keeps_the_template_and_the_secret_and_changes_the_rest()
    {
        var (connection, secret) = await CreateAsync(mappings: "/a => /b");

        var result = await _manager.UpdateAsync(connection.Id, new SaveWebhookConnectionRequest
        {
            Name = "Radarr HD", ProviderId = "sonarr_webhook", IsEnabled = false, HandleDeletes = false, PathMappings = "/movies => /media/movies",
        });

        result.Succeeded.Should().BeTrue();
        result.Value!.Name.Should().Be("Radarr HD");
        result.Value.ProviderId.Should().Be("radarr_webhook");
        result.Value.IsEnabled.Should().BeFalse();
        result.Value.HandleDeletes.Should().BeFalse();
        result.Value.PathMappings.Should().Be("/movies => /media/movies");
        (await _manager.GetConnectionAsync(connection.Id))!.Secret.Should().Be(secret);
        (await _manager.UpdateAsync(Guid.NewGuid(), Save("radarr_webhook"))).IsMissing.Should().BeTrue();
        (await _manager.UpdateAsync(connection.Id, Save("radarr_webhook", " "))).Error.Should().Contain("name");
    }

    [Fact]
    public async Task Regenerating_the_secret_retires_the_old_one()
    {
        var (connection, oldSecret) = await CreateAsync();

        var fresh = await _manager.RegenerateSecretAsync(connection.Id);

        fresh!.Secret.Should().NotBe(oldSecret);
        (await _manager.ReceiveAsync(connection.Id.ToString(), Import, new[] { oldSecret })).Status.Should().Be(MediaWebhookStatus.Unauthorized);
        (await _manager.ReceiveAsync(connection.Id.ToString(), Import, new[] { fresh.Secret })).Status.Should().Be(MediaWebhookStatus.Accepted);
        (await _manager.RegenerateSecretAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Deleting_a_connection_stops_it_answering()
    {
        var (connection, secret) = await CreateAsync();

        (await _manager.DeleteAsync(connection.Id)).Should().BeTrue();
        (await _manager.DeleteAsync(connection.Id)).Should().BeFalse();

        (await _manager.ReceiveAsync(connection.Id.ToString(), Import, new[] { secret })).Status.Should().Be(MediaWebhookStatus.NotFound);
    }

    [Fact]
    public async Task Two_connections_of_one_template_keep_their_own_secrets_and_mappings()
    {
        var (hd, hdSecret) = await CreateAsync(name: "Radarr HD", mappings: "/movies => /hd");
        var (fourK, fourKSecret) = await CreateAsync(name: "Radarr 4K", mappings: "/movies => /4k");

        (await _manager.ReceiveAsync(hd.Id.ToString(), Import, new[] { fourKSecret })).Status.Should().Be(MediaWebhookStatus.Unauthorized);
        (await _manager.ReceiveAsync(fourK.Id.ToString(), Import, new[] { fourKSecret })).Status.Should().Be(MediaWebhookStatus.Accepted);
        (await _manager.ReceiveAsync(hd.Id.ToString(), Import, new[] { hdSecret })).Status.Should().Be(MediaWebhookStatus.Accepted);

        _queue.Received(1).Enqueue(Arg.Is<MediaWebhookWork>(w => w.ConnectionName == "Radarr 4K" && w.PathMappings == "/movies => /4k"));
        _queue.Received(1).Enqueue(Arg.Is<MediaWebhookWork>(w => w.ConnectionName == "Radarr HD" && w.PathMappings == "/movies => /hd"));
    }

    [Fact]
    public async Task The_list_says_when_each_connection_last_heard_something()
    {
        var (connection, secret) = await CreateAsync();
        (await _manager.GetConnectionsAsync()).Single().LastDeliveryAt.Should().BeNull();

        await _manager.ReceiveAsync(connection.Id.ToString(), "{\"eventType\":\"Test\"}", new[] { secret });

        var listed = (await _manager.GetConnectionsAsync()).Single();
        listed.LastDeliveryAt.Should().NotBeNull();
        listed.LastDeliveryOk.Should().BeTrue();
        listed.ProviderName.Should().Be("Radarr Webhook");
    }

    [Fact]
    public void The_templates_are_listed_with_custom_last_and_what_each_needs()
    {
        var providers = _manager.GetProviders();

        providers.Select(p => p.Id).Should().Equal("lidarr_webhook", "radarr_webhook", "sonarr_webhook", "custom_webhook");
        providers.Single(p => p.Id == "radarr_webhook").LibraryKind.Should().Be("Movie");
        providers.Single(p => p.Id == "radarr_webhook").RequestServerProviderId.Should().Be("radarr_requester");
        providers.Single(p => p.Id == "radarr_webhook").SetupInstructions.Should().NotBeEmpty();
        providers.Single(p => p.Id == "custom_webhook").LibraryKind.Should().BeNull();
        providers.Single(p => p.Id == "custom_webhook").Settings.Select(s => s.Key).Should().Contain(new[] { "action", "event_path", "file_path" });
    }

    [Fact]
    public async Task A_sample_payload_shows_where_a_file_would_go_without_queueing_anything()
    {
        Directory.CreateDirectory(Path.Combine(_movies, "A (2020)"));
        File.WriteAllText(Path.Combine(_movies, "A (2020)", "A.mkv"), "x");

        var preview = await _manager.PreviewAsync(new WebhookPreviewRequest { ProviderId = "radarr_webhook", Body = Import });

        preview.Ok.Should().BeTrue();
        preview.Event.Should().Be("Download");
        preview.Instance.Should().Be("Radarr");
        var change = preview.Changes.Single();
        change.Kind.Should().Be("Added");
        change.MappedPath.Should().Be(Path.Combine(_movies, "A (2020)", "A.mkv"));
        change.LibraryName.Should().Be("Movies");
        change.ExistsOnDisk.Should().BeTrue();
        change.Note.Should().Be("Would be ingested.");
        _queue.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_sample_payload_that_cannot_be_placed_says_why()
    {
        var preview = await _manager.PreviewAsync(new WebhookPreviewRequest { ProviderId = "radarr_webhook", Body = Import.Replace("/movies/A (2020)", "/elsewhere/Unknown") });

        preview.Changes.Single().MappedPath.Should().BeNull();
        preview.Changes.Single().Note.Should().Contain("path mapping");
    }

    [Fact]
    public async Task A_sample_payload_is_judged_by_the_unsaved_settings()
    {
        var preview = await _manager.PreviewAsync(new WebhookPreviewRequest
        {
            ProviderId = "custom_webhook",
            Body = "{\"action\":\"done\",\"files\":[{\"p\":\"/movies/B/B.mkv\"}]}",
            Settings = new Dictionary<string, string>
            {
                [MediaWebhookSettingKeys.EventPath] = "action",
                [MediaWebhookSettingKeys.AddedEvents] = "done",
                [MediaWebhookSettingKeys.FilePath] = "files[].p",
            },
        });

        preview.Event.Should().Be("done");
        preview.Changes.Single().ReportedPath.Should().Be("/movies/B/B.mkv");
    }

    [Fact]
    public async Task A_sample_scan_payload_names_the_library_it_would_scan()
    {
        var preview = await _manager.PreviewAsync(new WebhookPreviewRequest
        {
            ProviderId = "custom_webhook",
            Body = "{\"event\":\"added\"}",
            Settings = new Dictionary<string, string>
            {
                [MediaWebhookSettingKeys.Action] = MediaWebhookSettingKeys.ScanAction,
                [MediaWebhookSettingKeys.ScanLibrary] = _tvLibrary.ToString(),
            },
        });

        preview.Changes.Single().Kind.Should().Be("ScanLibrary");
        preview.Changes.Single().Note.Should().Be("Would scan TV.");
    }

    [Fact]
    public async Task A_sample_that_is_not_json_or_has_no_template_is_reported_not_thrown()
    {
        (await _manager.PreviewAsync(new WebhookPreviewRequest { ProviderId = "radarr_webhook", Body = "oops" })).Error.Should().Contain("not valid JSON");
        (await _manager.PreviewAsync(new WebhookPreviewRequest { ProviderId = "", Body = Import })).Error.Should().Contain("template");
        (await _manager.PreviewAsync(new WebhookPreviewRequest { ProviderId = "radarr_webhook", Body = "{\"eventType\":\"Test\"}" })).IsTest.Should().BeTrue();
    }

    private RequestServerVM Server(string name, string provider, bool enabled = true) =>
        new() { Id = Guid.NewGuid(), Name = name, ProviderId = provider, Hostname = "radarr.lan", Port = 7878, ApiKey = "key", UrlBase = "/radarr", IsEnabled = enabled };

    [Fact]
    public async Task Only_enabled_radarr_and_sonarr_request_servers_are_offered_for_filling_in()
    {
        var radarr = Server("Radarr 4K", "radarr_requester");
        var sonarr = Server("Sonarr", "sonarr_requester");
        _requests.GetAllServersAsync().Returns(new List<RequestServerVM> { radarr, sonarr, Server("Off", "radarr_requester", enabled: false), Server("Other", "something_else") });

        var servers = await _manager.GetRequestServersAsync();

        servers.Select(s => (s.Name, s.WebhookProviderId)).Should().Equal(("Radarr 4K", "radarr_webhook"), ("Sonarr", "sonarr_webhook"));
    }

    [Fact]
    public async Task Filling_in_from_a_request_server_asks_it_for_its_root_folders_and_suggests_the_library_folders()
    {
        var radarr = Server("Radarr 4K", "radarr_requester");
        _requests.GetAllServersAsync().Returns(new List<RequestServerVM> { radarr });
        _arr.GetRootFoldersAsync("http://radarr.lan:7878/radarr", "key", Arg.Any<CancellationToken>()).Returns(new[] { "/data/movies", "/data/other" });

        var suggestion = await _manager.SuggestMappingsAsync(radarr.Id);

        suggestion!.Name.Should().Be("Radarr 4K");
        suggestion.Error.Should().BeNull();
        suggestion.RootFolders.Should().Equal("/data/movies", "/data/other");
        suggestion.Mappings.Should().Be($"/data/movies => {_movies}\n/data/other => {_movies}");
    }

    [Fact]
    public async Task A_request_server_that_cannot_be_reached_is_reported_and_fills_nothing()
    {
        var radarr = Server("Radarr", "radarr_requester");
        _requests.GetAllServersAsync().Returns(new List<RequestServerVM> { radarr });
        _arr.GetRootFoldersAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<string>>(_ => throw new HttpRequestException("Connection refused"));

        var suggestion = await _manager.SuggestMappingsAsync(radarr.Id);

        suggestion!.Error.Should().Contain("Connection refused");
        suggestion.Mappings.Should().BeEmpty();
        (await _manager.SuggestMappingsAsync(Guid.NewGuid())).Should().BeNull();
    }
}
