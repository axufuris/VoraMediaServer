using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Custom;

namespace Vora.Application.Tests.Webhooks;

public class CustomWebhookProviderTests
{
    private readonly CustomWebhookProvider _provider = new();

    private IReadOnlyDictionary<string, string> Defaults() =>
        _provider.GetConnectionSettingDefinitions().ToDictionary(d => d.Key, d => d.DefaultValue);

    private IReadOnlyDictionary<string, string> With(params (string Key, string Value)[] overrides)
    {
        var settings = Defaults().ToDictionary(p => p.Key, p => p.Value);
        foreach (var (key, value) in overrides) settings[key] = value;
        return settings;
    }

    private static MediaWebhookChange Added(string path) => new(MediaWebhookChangeKind.Added, path);

    private static MediaWebhookChange Removed(string path) => new(MediaWebhookChangeKind.Removed, path);

    private static MediaWebhookChange Folder(string path) => new(MediaWebhookChangeKind.FolderRemoved, path);

    [Fact]
    public void It_is_a_template_for_any_kind_of_library_that_a_connection_chooses()
    {
        _provider.Id.Should().Be("custom_webhook");
        _provider.Type.Should().Be("MediaWebhook");
        _provider.GetSettingDefinitions().Should().BeEmpty();
        ((IMediaWebhookProvider)_provider).RequestServerProviderId.Should().BeNull();
        _provider.SetupInstructions.Should().Contain("Basic auth");
    }

    [Fact]
    public void Its_settings_hide_what_the_chosen_action_does_not_use()
    {
        var definitions = _provider.GetConnectionSettingDefinitions().ToDictionary(d => d.Key);

        definitions["action"].Type.Should().Be("select");
        definitions["action"].Options.Should().Equal("ingest=Ingest or remove the file named in the payload", "scan=Scan a library");
        definitions["library_type"].ShowWhen.Should().Be("action=ingest");
        definitions["file_path"].ShowWhen.Should().Be("action=ingest");
        definitions["scan_library"].ShowWhen.Should().Be("action=scan");
        definitions["scan_library"].Type.Should().Be("library");
        definitions["scan_library"].Required.Should().BeTrue();
    }

    [Fact]
    public void Which_kind_of_library_comes_from_the_connection_or_is_none_when_it_scans()
    {
        _provider.ResolveLibraryKind(With(("library_type", "TvShow"))).Should().Be(LibraryKind.TvShow);
        _provider.ResolveLibraryKind(With(("library_type", "music"))).Should().Be(LibraryKind.Music);
        _provider.ResolveLibraryKind(With(("action", "scan"), ("library_type", "TvShow"))).Should().BeNull();
        _provider.ResolveLibraryKind(With(("library_type", "nonsense"))).Should().BeNull();
        _provider.ResolveLibraryKind(new Dictionary<string, string>()).Should().BeNull();
    }

    [Fact]
    public void The_defaults_suit_a_simple_script_that_posts_an_event_and_a_path()
    {
        _provider.Parse("{\"event\":\"added\",\"path\":\"/media/movies/Film/Film.mkv\"}", Defaults()).Changes
            .Should().Equal(Added("/media/movies/Film/Film.mkv"));
        _provider.Parse("{\"event\":\"removed\",\"path\":\"/media/movies/Film/Film.mkv\"}", Defaults()).Changes
            .Should().Equal(Removed("/media/movies/Film/Film.mkv"));
        _provider.Parse("{\"event\":\"folder_removed\",\"folder\":\"/media/tv/Show\"}", Defaults()).Changes
            .Should().Equal(Folder("/media/tv/Show"));
    }

    [Fact]
    public void Event_names_match_in_any_case_and_a_list_of_names_is_allowed()
    {
        var settings = With(("added_events", "Import, Download"), ("removed_events", "Deleted"));

        _provider.Parse("{\"event\":\"DOWNLOAD\",\"path\":\"/a.mkv\"}", settings).Changes.Should().Equal(Added("/a.mkv"));
        _provider.Parse("{\"event\":\"deleted\",\"path\":\"/a.mkv\"}", settings).Changes.Should().Equal(Removed("/a.mkv"));
    }

    [Fact]
    public void A_test_is_a_test_and_an_event_it_does_not_know_is_ignored()
    {
        _provider.Parse("{\"event\":\"test\"}", Defaults()).IsTest.Should().BeTrue();

        var ignored = _provider.Parse("{\"event\":\"grabbed\",\"path\":\"/a.mkv\"}", Defaults());
        ignored.IsTest.Should().BeFalse();
        ignored.Changes.Should().BeEmpty();
        ignored.EventName.Should().Be("grabbed");
        _provider.Parse("{\"path\":\"/a.mkv\"}", Defaults()).Changes.Should().BeEmpty();
    }

    [Fact]
    public void With_no_event_field_every_request_is_a_file_added()
    {
        var settings = With(("event_path", string.Empty));

        _provider.Parse("{\"path\":\"/a.mkv\"}", settings).Changes.Should().Equal(Added("/a.mkv"));
    }

    [Fact]
    public void Paths_are_read_from_nested_fields_and_lists_and_replaced_files_are_removed()
    {
        var settings = With(
            ("event_path", "eventType"), ("added_events", "Download"),
            ("file_path", "episodeFiles[].path, episodeFile.path"), ("also_removed_path", "deletedFiles[].path"));

        var result = _provider.Parse("""
            {"eventType":"Download","episodeFiles":[{"path":"/tv/S1/e1.mkv"},{"path":"/tv/S1/e2.mkv"}],
             "deletedFiles":[{"path":"/tv/S1/old.mkv"},{"path":"/tv/S1/e1.mkv"}]}
            """, settings);

        result.Changes.Should().Equal(Added("/tv/S1/e1.mkv"), Added("/tv/S1/e2.mkv"), Removed("/tv/S1/old.mkv"));
    }

    [Fact]
    public void A_scan_connection_scans_on_any_event_it_knows_and_ignores_the_rest()
    {
        var settings = With(("action", "scan"));

        _provider.Parse("{\"event\":\"added\"}", settings).Changes.Should().Equal(new MediaWebhookChange(MediaWebhookChangeKind.ScanLibrary, string.Empty));
        _provider.Parse("{\"event\":\"removed\"}", settings).Changes.Should().HaveCount(1);
        _provider.Parse("{\"event\":\"something\"}", settings).Changes.Should().BeEmpty();
        _provider.Parse("{\"event\":\"test\"}", settings).IsTest.Should().BeTrue();
    }

    [Fact]
    public void A_body_that_is_not_json_is_refused()
    {
        var parse = () => _provider.Parse("not json", Defaults());

        parse.Should().Throw<JsonException>();
    }

    [Fact]
    public void Json_paths_go_down_by_dots_expand_lists_by_brackets_and_ignore_case()
    {
        using var document = JsonDocument.Parse("""{"A":{"b":[{"c":"one"},{"c":"two"},{"d":"x"}],"n":3,"t":true},"list":["p","q"]}""");
        var root = document.RootElement;

        JsonValuePaths.Values(root, "a.b[].c").Should().Equal("one", "two");
        JsonValuePaths.Values(root, "$.a.n").Should().Equal("3");
        JsonValuePaths.Values(root, "a.t").Should().Equal("true");
        JsonValuePaths.Values(root, "list").Should().Equal("p", "q");
        JsonValuePaths.Values(root, "a.b[].c, list").Should().Equal("one", "two", "p", "q");
        JsonValuePaths.Values(root, "a.missing.deeper").Should().BeEmpty();
        JsonValuePaths.Values(root, "a.n[]").Should().BeEmpty();
        JsonValuePaths.Values(root, "  ").Should().BeEmpty();
        JsonValuePaths.First(root, "a.b[].c").Should().Be("one");
        JsonValuePaths.First(root, "nope").Should().BeNull();
    }
}
