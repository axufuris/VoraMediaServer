using Vora.Application.Settings;

namespace Vora.Application.Tests.Settings;

public sealed class StorageRootsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-storage-roots-" + Guid.NewGuid().ToString("N"));
    private readonly string _data;
    private readonly string _app;

    public StorageRootsTests()
    {
        _data = Path.Combine(_root, "data");
        _app = Path.Combine(_root, "app");
        Directory.CreateDirectory(_data);
        Directory.CreateDirectory(_app);
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

    private StoragePathsOptions ReadmeCompose() => new()
    {
        CustomArtwork = Path.Combine(_data, "custom_artwork"),
        OriginalArtworkCache = Path.Combine(_data, "original_artwork_cache"),
        EpgCache = Path.Combine(_data, "iptv", "epg_cache"),
        IptvDvr = Path.Combine(_data, "iptv", "dvr"),
        UserImages = Path.Combine(_data, "users"),
        Plugins = Path.Combine(_data, "plugins"),
        Subtitles = Path.Combine(_data, "subtitles")
    };

    [Fact]
    public void A_folder_that_is_set_is_used_as_given()
    {
        var paths = ReadmeCompose();
        paths.VideoThumbnails = Path.Combine(_root, "elsewhere", "thumbs");
        paths.Logs = Path.Combine(_root, "elsewhere", "logs");
        paths.Backups = Path.Combine(_root, "elsewhere", "backups");
        paths.DataProtection = Path.Combine(_root, "elsewhere", "keys");

        StorageRoots.VideoThumbnails(paths).Should().Be(paths.VideoThumbnails);
        StorageRoots.Logs(paths).Should().Be(paths.Logs);
        StorageRoots.Backups(paths).Should().Be(paths.Backups);
        StorageRoots.DataProtection(paths).Should().Be(paths.DataProtection);
    }

    [Fact]
    public void The_data_folder_is_where_the_configured_folders_sit()
    {
        StorageRoots.DataFolder(ReadmeCompose()).Should().Be(_data);
    }

    [Fact]
    public void A_trailing_separator_does_not_change_the_data_folder()
    {
        var paths = new StoragePathsOptions { CustomArtwork = Path.Combine(_data, "custom_artwork") + Path.DirectorySeparatorChar };

        StorageRoots.DataFolder(paths).Should().Be(_data);
    }

    [Fact]
    public void Folders_nested_deeper_do_not_decide_the_data_folder()
    {
        var paths = new StoragePathsOptions
        {
            EpgCache = Path.Combine(_data, "iptv", "epg_cache"),
            IptvDvr = Path.Combine(_data, "iptv", "dvr")
        };

        StorageRoots.DataFolder(paths).Should().BeNull();
    }

    [Theory]
    [InlineData("video-thumbnails", "video-thumbnails")]
    [InlineData("logs", "logs")]
    [InlineData("backups", "backups")]
    [InlineData("DataProtectionKeys", "dp-keys")]
    public void A_folder_that_is_not_set_goes_in_the_data_folder(string nameInAppFolder, string nameInDataFolder)
    {
        StorageRoots.InDataFolder(ReadmeCompose(), _app, nameInAppFolder, nameInDataFolder)
            .Should().Be(Path.Combine(_data, nameInDataFolder));
    }

    [Fact]
    public void A_folder_already_in_use_beside_the_app_is_kept()
    {
        var existing = Path.Combine(_app, "DataProtectionKeys");
        Directory.CreateDirectory(existing);

        StorageRoots.InDataFolder(ReadmeCompose(), _app, "DataProtectionKeys", "dp-keys").Should().Be(existing);
    }

    [Fact]
    public void With_nothing_configured_and_no_data_folder_the_folder_stays_beside_the_app()
    {
        StorageRoots.InDataFolder(new StoragePathsOptions(), _app, "video-thumbnails", "video-thumbnails")
            .Should().Be(Path.Combine(_app, "video-thumbnails"));
        StorageRoots.DataFolder(new StoragePathsOptions(), _app).Should().BeNull();
    }

    [Fact]
    public void With_nothing_configured_the_data_folder_the_image_ships_is_used()
    {
        var shipped = Path.Combine(_app, "data");
        Directory.CreateDirectory(shipped);

        StorageRoots.DataFolder(new StoragePathsOptions(), _app).Should().Be(shipped);
        StorageRoots.InDataFolder(new StoragePathsOptions(), _app, Path.Combine("Storage", "CustomArtwork"), "custom_artwork")
            .Should().Be(Path.Combine(shipped, "custom_artwork"));
        StorageRoots.InDataFolder(new StoragePathsOptions(), _app, "logs", "logs").Should().Be(Path.Combine(shipped, "logs"));
    }

    [Fact]
    public void The_answer_does_not_change_while_the_server_runs()
    {
        var first = StorageRoots.InDataFolder(new StoragePathsOptions(), _app, "Users", "users");
        Directory.CreateDirectory(Path.Combine(_app, "data"));

        StorageRoots.InDataFolder(new StoragePathsOptions(), _app, "Users", "users").Should().Be(first);
    }

    [Fact]
    public void Every_folder_left_out_lands_in_the_data_folder_under_the_name_the_compose_file_uses()
    {
        var paths = new StoragePathsOptions { Subtitles = Path.Combine(_data, "subtitles") };

        StorageRoots.CustomArtwork(paths, _app).Should().Be(Path.Combine(_data, "custom_artwork"));
        StorageRoots.OriginalArtworkCache(paths, _app).Should().Be(Path.Combine(_data, "original_artwork_cache"));
        StorageRoots.UserImages(paths, _app).Should().Be(Path.Combine(_data, "users"));
        StorageRoots.Plugins(paths, _app).Should().Be(Path.Combine(_data, "plugins"));
        StorageRoots.EpgCache(paths, _app).Should().Be(Path.Combine(_data, "iptv", "epg_cache"));
        StorageRoots.IptvDvr(paths, null, _app).Should().Be(Path.Combine(_data, "iptv", "dvr"));
        StorageRoots.AudioFingerprints(paths, _app).Should().Be(Path.Combine(_data, "audiofingerprints"));
    }

    [Fact]
    public void The_dvr_folder_chosen_in_settings_wins_over_the_environment()
    {
        var paths = ReadmeCompose();
        var chosen = Path.Combine(_root, "recordings");

        StorageRoots.IptvDvr(paths, chosen).Should().Be(chosen);
        StorageRoots.IptvDvr(paths, configuredInSettings: " ").Should().Be(paths.IptvDvr);
    }

    [Fact]
    public void Every_storage_folder_is_listed_once_for_the_startup_check()
    {
        var folders = StorageRoots.All(ReadmeCompose(), null, _app);

        folders.Select(f => f.Setting).Should().OnlyHaveUniqueItems();
        folders.Should().Contain((nameof(StoragePathsOptions.VideoThumbnails), Path.Combine(_data, "video-thumbnails")));
        folders.Should().Contain((nameof(StoragePathsOptions.CustomArtwork), Path.Combine(_data, "custom_artwork")));
        folders.Should().HaveCount(11);
    }

    [Fact]
    public void The_public_resolvers_use_the_data_folder()
    {
        var paths = ReadmeCompose();

        StorageRoots.VideoThumbnails(paths, _app).Should().Be(Path.Combine(_data, "video-thumbnails"));
        StorageRoots.Logs(paths, _app).Should().Be(Path.Combine(_data, "logs"));
        StorageRoots.Backups(paths, _app).Should().Be(Path.Combine(_data, "backups"));
        StorageRoots.DataProtection(paths, _app).Should().Be(Path.Combine(_data, "dp-keys"));
    }
}
