using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Providers.Lidarr;
using Vora.Plugins.Providers.Radarr;
using Vora.Plugins.Providers.Sonarr;

namespace Vora.Application.Tests.Webhooks;

public class ArrWebhookProviderTests
{
    private static MediaWebhookChange Added(string path) => new(MediaWebhookChangeKind.Added, path);

    private static MediaWebhookChange Removed(string path) => new(MediaWebhookChangeKind.Removed, path);

    private static MediaWebhookChange Folder(string path) => new(MediaWebhookChangeKind.FolderRemoved, path);

    private readonly RadarrWebhookProvider _radarr = new();
    private readonly SonarrWebhookProvider _sonarr = new();
    private readonly LidarrWebhookProvider _lidarr = new();

    [Fact]
    public void Each_provider_is_its_own_plugin_for_one_kind_of_library_with_a_secret_and_path_mappings()
    {
        _radarr.Id.Should().Be("radarr_webhook");
        _sonarr.Id.Should().Be("sonarr_webhook");
        _lidarr.Id.Should().Be("lidarr_webhook");
        new[] { _radarr.Type, _sonarr.Type, _lidarr.Type }.Should().OnlyContain(t => t == "MediaWebhook");
        _radarr.SupportedLibraryKinds.Should().Equal(LibraryKind.Movie);
        _sonarr.SupportedLibraryKinds.Should().Equal(LibraryKind.TvShow);
        _lidarr.SupportedLibraryKinds.Should().Equal(LibraryKind.Music);

        var definitions = _radarr.GetSettingDefinitions().ToList();
        definitions.Select(d => d.Key).Should().Equal(MediaWebhookSettingKeys.Secret, MediaWebhookSettingKeys.PathMappings);
        definitions.Single(d => d.Key == MediaWebhookSettingKeys.Secret).Required.Should().BeTrue();
        definitions.Single(d => d.Key == MediaWebhookSettingKeys.Secret).Type.Should().Be("password");
        definitions.Single(d => d.Key == MediaWebhookSettingKeys.PathMappings).Required.Should().BeFalse();
    }

    [Fact]
    public void A_test_from_any_of_them_is_a_test()
    {
        var radarr = _radarr.Parse("{\"eventType\":\"Test\",\"instanceName\":\"Radarr 4K\"}");
        var sonarr = _sonarr.Parse("{\"eventType\":\"Test\"}");
        var lidarr = _lidarr.Parse("{\"eventType\":\"Test\"}");

        radarr.IsTest.Should().BeTrue();
        radarr.InstanceName.Should().Be("Radarr 4K");
        sonarr.IsTest.Should().BeTrue();
        lidarr.IsTest.Should().BeTrue();
    }

    [Fact]
    public void Property_names_are_read_whatever_their_case()
    {
        var result = _radarr.Parse("{\"EventType\":\"Download\",\"MovieFile\":{\"Path\":\"/movies/A (2020)/A.mkv\"}}");

        result.Changes.Should().Equal(Added("/movies/A (2020)/A.mkv"));
    }

    [Fact]
    public void Events_that_change_no_files_are_ignored()
    {
        foreach (var name in new[] { "Grab", "MovieAdded", "Health", "HealthRestored", "ApplicationUpdate", "ManualInteractionRequired", "Surprise" })
        {
            var result = _radarr.Parse($"{{\"eventType\":\"{name}\"}}");

            result.IsTest.Should().BeFalse();
            result.Changes.Should().BeEmpty();
            result.EventName.Should().Be(name);
        }
    }

    [Fact]
    public void A_body_that_is_not_json_is_refused_rather_than_ignored()
    {
        var parse = () => _radarr.Parse("not json");

        parse.Should().Throw<JsonException>();
    }

    [Fact]
    public void Radarr_import_adds_the_new_file()
    {
        var result = _radarr.Parse("""
            {"eventType":"Download","instanceName":"Radarr","movie":{"id":1,"title":"A","folderPath":"/movies/A (2020)"},
             "movieFile":{"id":9,"relativePath":"A.mkv","path":"/movies/A (2020)/A.mkv"},"isUpgrade":false}
            """);

        result.Changes.Should().Equal(Added("/movies/A (2020)/A.mkv"));
    }

    [Fact]
    public void Radarr_upgrade_adds_the_new_file_and_removes_the_ones_it_replaced()
    {
        var result = _radarr.Parse("""
            {"eventType":"Download","isUpgrade":true,
             "movieFile":{"path":"/movies/A (2020)/A.2160p.mkv"},
             "deletedFiles":[{"path":"/movies/A (2020)/A.1080p.mkv"},{"path":"/movies/A (2020)/A.2160p.mkv"}]}
            """);

        result.Changes.Should().Equal(Added("/movies/A (2020)/A.2160p.mkv"), Removed("/movies/A (2020)/A.1080p.mkv"));
    }

    [Fact]
    public void Radarr_rename_adds_the_new_name_and_removes_the_old_one()
    {
        var result = _radarr.Parse("""
            {"eventType":"Rename","renamedMovieFiles":[
              {"path":"/movies/A (2020)/A (2020).mkv","previousPath":"/movies/A (2020)/a.mkv"},
              {"path":"/movies/B (2021)/B.mkv","previousPath":"/movies/B (2021)/B.mkv"}]}
            """);

        result.Changes.Should().Equal(
            Added("/movies/A (2020)/A (2020).mkv"), Removed("/movies/A (2020)/a.mkv"),
            Added("/movies/B (2021)/B.mkv"));
    }

    [Fact]
    public void Radarr_deletes_remove_a_file_or_a_whole_folder_only_when_files_were_deleted()
    {
        _radarr.Parse("{\"eventType\":\"MovieFileDelete\",\"movieFile\":{\"path\":\"/movies/A (2020)/A.mkv\"}}").Changes
            .Should().Equal(Removed("/movies/A (2020)/A.mkv"));
        _radarr.Parse("{\"eventType\":\"MovieDelete\",\"deletedFiles\":true,\"movie\":{\"folderPath\":\"/movies/A (2020)\"}}").Changes
            .Should().Equal(Folder("/movies/A (2020)"));
        _radarr.Parse("{\"eventType\":\"MovieDelete\",\"deletedFiles\":false,\"movie\":{\"folderPath\":\"/movies/A (2020)\"}}").Changes
            .Should().BeEmpty();
    }

    [Fact]
    public void Sonarr_file_import_has_one_episode_file()
    {
        var result = _sonarr.Parse("""
            {"eventType":"Download","series":{"path":"/tv/Show"},
             "episodeFile":{"relativePath":"Season 1/Show - S01E01.mkv","path":"/tv/Show/Season 1/Show - S01E01.mkv"}}
            """);

        result.Changes.Should().Equal(Added("/tv/Show/Season 1/Show - S01E01.mkv"));
    }

    [Fact]
    public void Sonarr_import_complete_has_a_list_of_episode_files_under_the_same_event_name()
    {
        var result = _sonarr.Parse("""
            {"eventType":"Download","series":{"path":"/tv/Show"},
             "episodeFiles":[{"path":"/tv/Show/Season 1/e1.mkv"},{"path":"/tv/Show/Season 1/e2.mkv"},{"path":"/tv/Show/Season 1/e1.mkv"}],
             "sourcePath":"/downloads/Show","destinationPath":"/tv/Show/Season 1"}
            """);

        result.Changes.Should().Equal(Added("/tv/Show/Season 1/e1.mkv"), Added("/tv/Show/Season 1/e2.mkv"));
    }

    [Fact]
    public void Sonarr_upgrade_removes_the_replaced_file_and_rename_moves_it()
    {
        _sonarr.Parse("""
            {"eventType":"Download","isUpgrade":true,"episodeFile":{"path":"/tv/Show/S1/new.mkv"},"deletedFiles":[{"path":"/tv/Show/S1/old.mkv"}]}
            """).Changes.Should().Equal(Added("/tv/Show/S1/new.mkv"), Removed("/tv/Show/S1/old.mkv"));

        _sonarr.Parse("""
            {"eventType":"Rename","renamedEpisodeFiles":[{"path":"/tv/Show/S1/new name.mkv","previousPath":"/tv/Show/S1/old name.mkv"}]}
            """).Changes.Should().Equal(Added("/tv/Show/S1/new name.mkv"), Removed("/tv/Show/S1/old name.mkv"));
    }

    [Fact]
    public void Sonarr_deletes_remove_a_file_or_a_series_folder_only_when_files_were_deleted()
    {
        _sonarr.Parse("{\"eventType\":\"EpisodeFileDelete\",\"episodeFile\":{\"path\":\"/tv/Show/S1/e1.mkv\"},\"deleteReason\":\"Manual\"}").Changes
            .Should().Equal(Removed("/tv/Show/S1/e1.mkv"));
        _sonarr.Parse("{\"eventType\":\"SeriesDelete\",\"deletedFiles\":true,\"series\":{\"path\":\"/tv/Show\"}}").Changes
            .Should().Equal(Folder("/tv/Show"));
        _sonarr.Parse("{\"eventType\":\"SeriesDelete\",\"deletedFiles\":false,\"series\":{\"path\":\"/tv/Show\"}}").Changes
            .Should().BeEmpty();
        _sonarr.Parse("{\"eventType\":\"SeriesAdd\",\"series\":{\"path\":\"/tv/Show\"}}").Changes.Should().BeEmpty();
    }

    [Fact]
    public void Lidarr_import_upgrade_rename_and_retag_name_track_files()
    {
        _lidarr.Parse("""
            {"eventType":"Download","isUpgrade":true,"artist":{"path":"/music/Artist"},
             "trackFiles":[{"path":"/music/Artist/Album/01.flac"},{"path":"/music/Artist/Album/02.flac"}],
             "deletedFiles":[{"path":"/music/Artist/Album/01.mp3"}]}
            """).Changes.Should().Equal(Added("/music/Artist/Album/01.flac"), Added("/music/Artist/Album/02.flac"), Removed("/music/Artist/Album/01.mp3"));

        _lidarr.Parse("""
            {"eventType":"Rename","renamedTrackFiles":[{"path":"/music/Artist/Album/01 - A.flac","previousPath":"/music/Artist/Album/01.flac"}]}
            """).Changes.Should().Equal(Added("/music/Artist/Album/01 - A.flac"), Removed("/music/Artist/Album/01.flac"));

        _lidarr.Parse("{\"eventType\":\"Retag\",\"trackFile\":{\"path\":\"/music/Artist/Album/01.flac\"}}").Changes
            .Should().Equal(Added("/music/Artist/Album/01.flac"));
    }

    [Fact]
    public void Lidarr_artist_and_album_deletes_sweep_the_artist_folder_only_when_files_were_deleted()
    {
        _lidarr.Parse("{\"eventType\":\"ArtistDelete\",\"deletedFiles\":true,\"artist\":{\"path\":\"/music/Artist\"}}").Changes
            .Should().Equal(Folder("/music/Artist"));
        _lidarr.Parse("{\"eventType\":\"AlbumDelete\",\"deletedFiles\":true,\"artist\":{\"path\":\"/music/Artist\"},\"album\":{\"title\":\"X\"}}").Changes
            .Should().Equal(Folder("/music/Artist"));
        _lidarr.Parse("{\"eventType\":\"AlbumDelete\",\"deletedFiles\":false,\"artist\":{\"path\":\"/music/Artist\"}}").Changes
            .Should().BeEmpty();
    }
}
