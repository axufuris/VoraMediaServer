using Vora.Application.Settings;

namespace Vora.Application.Tests.Settings;

public class StorageRootsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "vora-storage-roots");
    private static readonly string Data = Path.Combine(Root, "data");
    private static readonly string App = Path.Combine(Root, "app");

    private static StoragePathsOptions ReadmeCompose() => new()
    {
        CustomArtwork = Path.Combine(Data, "custom_artwork"),
        OriginalArtworkCache = Path.Combine(Data, "original_artwork_cache"),
        EpgCache = Path.Combine(Data, "iptv", "epg_cache"),
        IptvDvr = Path.Combine(Data, "iptv", "dvr"),
        UserImages = Path.Combine(Data, "users"),
        Plugins = Path.Combine(Data, "plugins"),
        Subtitles = Path.Combine(Data, "subtitles")
    };

    private static Dictionary<string, string> Folders(StoragePathsOptions paths) =>
        StorageRoots.All(paths, null, App).ToDictionary(f => f.Setting, f => f.Folder);

    [Fact]
    public void A_folder_that_is_set_is_used_as_given()
    {
        var elsewhere = Path.Combine(Root, "elsewhere");
        var paths = new StoragePathsOptions
        {
            CustomArtwork = Path.Combine(elsewhere, "art"),
            OriginalArtworkCache = Path.Combine(elsewhere, "originals"),
            UserImages = Path.Combine(elsewhere, "people"),
            Plugins = Path.Combine(elsewhere, "addons"),
            EpgCache = Path.Combine(elsewhere, "guide"),
            IptvDvr = Path.Combine(elsewhere, "recordings"),
            VideoThumbnails = Path.Combine(elsewhere, "thumbs"),
            AudioFingerprints = Path.Combine(elsewhere, "prints"),
            Logs = Path.Combine(elsewhere, "log"),
            Backups = Path.Combine(elsewhere, "saved"),
            DataProtection = Path.Combine(elsewhere, "keys")
        };

        Folders(paths).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            [nameof(StoragePathsOptions.CustomArtwork)] = paths.CustomArtwork,
            [nameof(StoragePathsOptions.OriginalArtworkCache)] = paths.OriginalArtworkCache,
            [nameof(StoragePathsOptions.UserImages)] = paths.UserImages,
            [nameof(StoragePathsOptions.Plugins)] = paths.Plugins,
            [nameof(StoragePathsOptions.EpgCache)] = paths.EpgCache,
            [nameof(StoragePathsOptions.IptvDvr)] = paths.IptvDvr,
            [nameof(StoragePathsOptions.VideoThumbnails)] = paths.VideoThumbnails,
            [nameof(StoragePathsOptions.AudioFingerprints)] = paths.AudioFingerprints,
            [nameof(StoragePathsOptions.Logs)] = paths.Logs,
            [nameof(StoragePathsOptions.Backups)] = paths.Backups,
            [nameof(StoragePathsOptions.DataProtection)] = paths.DataProtection
        });
    }

    [Fact]
    public void One_folder_that_is_set_shows_where_all_the_others_go()
    {
        var paths = new StoragePathsOptions { CustomArtwork = Path.Combine(Data, "custom_artwork") };

        Folders(paths).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            [nameof(StoragePathsOptions.CustomArtwork)] = Path.Combine(Data, "custom_artwork"),
            [nameof(StoragePathsOptions.OriginalArtworkCache)] = Path.Combine(Data, "original_artwork_cache"),
            [nameof(StoragePathsOptions.UserImages)] = Path.Combine(Data, "users"),
            [nameof(StoragePathsOptions.Plugins)] = Path.Combine(Data, "plugins"),
            [nameof(StoragePathsOptions.EpgCache)] = Path.Combine(Data, "iptv", "epg_cache"),
            [nameof(StoragePathsOptions.IptvDvr)] = Path.Combine(Data, "iptv", "dvr"),
            [nameof(StoragePathsOptions.VideoThumbnails)] = Path.Combine(Data, "video-thumbnails"),
            [nameof(StoragePathsOptions.AudioFingerprints)] = Path.Combine(Data, "audiofingerprints"),
            [nameof(StoragePathsOptions.Logs)] = Path.Combine(Data, "logs"),
            [nameof(StoragePathsOptions.Backups)] = Path.Combine(Data, "backups"),
            [nameof(StoragePathsOptions.DataProtection)] = Path.Combine(Data, "dp-keys")
        });
    }

    [Fact]
    public void The_folders_the_install_examples_left_out_join_the_ones_they_set()
    {
        var folders = Folders(ReadmeCompose());

        folders[nameof(StoragePathsOptions.VideoThumbnails)].Should().Be(Path.Combine(Data, "video-thumbnails"));
        folders[nameof(StoragePathsOptions.Logs)].Should().Be(Path.Combine(Data, "logs"));
        folders[nameof(StoragePathsOptions.Backups)].Should().Be(Path.Combine(Data, "backups"));
        folders[nameof(StoragePathsOptions.DataProtection)].Should().Be(Path.Combine(Data, "dp-keys"));
        folders[nameof(StoragePathsOptions.AudioFingerprints)].Should().Be(Path.Combine(Data, "audiofingerprints"));
    }

    [Fact]
    public void With_nothing_set_everything_goes_in_the_data_folder_beside_the_app()
    {
        var shipped = Path.Combine(App, "data");

        StorageRoots.DataFolder(new StoragePathsOptions(), App).Should().Be(shipped);
        Folders(new StoragePathsOptions()).Values.Should().OnlyContain(folder => folder.StartsWith(shipped + Path.DirectorySeparatorChar));
        Folders(new StoragePathsOptions())[nameof(StoragePathsOptions.CustomArtwork)].Should().Be(Path.Combine(shipped, "custom_artwork"));
    }

    [Fact]
    public void No_folder_is_ever_placed_directly_in_the_app_folder()
    {
        foreach (var paths in new[] { new StoragePathsOptions(), ReadmeCompose(), new StoragePathsOptions { Logs = Path.Combine(Data, "logs") } })
        {
            Folders(paths).Values.Should().NotContain(folder => Path.GetDirectoryName(folder) == App);
        }
    }

    [Fact]
    public void A_trailing_separator_does_not_change_the_data_folder()
    {
        var paths = new StoragePathsOptions { CustomArtwork = Path.Combine(Data, "custom_artwork") + Path.DirectorySeparatorChar };

        StorageRoots.DataFolder(paths, App).Should().Be(Data);
    }

    [Fact]
    public void Folders_nested_deeper_do_not_decide_the_data_folder()
    {
        var paths = new StoragePathsOptions
        {
            EpgCache = Path.Combine(Data, "iptv", "epg_cache"),
            IptvDvr = Path.Combine(Data, "iptv", "dvr")
        };

        StorageRoots.DataFolder(paths, App).Should().Be(Path.Combine(App, "data"));
    }

    [Fact]
    public void The_dvr_folder_chosen_in_settings_wins_over_the_environment()
    {
        var paths = ReadmeCompose();
        var chosen = Path.Combine(Root, "recordings");

        StorageRoots.IptvDvr(paths, chosen, App).Should().Be(chosen);
        StorageRoots.IptvDvr(paths, " ", App).Should().Be(paths.IptvDvr);
    }

    [Fact]
    public void Every_storage_folder_is_listed_once_for_the_startup_check()
    {
        var folders = StorageRoots.All(ReadmeCompose(), null, App);

        folders.Select(f => f.Setting).Should().OnlyHaveUniqueItems();
        folders.Should().HaveCount(11);
    }

    [Fact]
    public void The_running_server_uses_its_own_app_folder()
    {
        StorageRoots.DataFolder(new StoragePathsOptions()).Should().Be(Path.Combine(AppContext.BaseDirectory, "data"));
        StorageRoots.VideoThumbnails(new StoragePathsOptions()).Should().Be(Path.Combine(AppContext.BaseDirectory, "data", "video-thumbnails"));
    }
}
