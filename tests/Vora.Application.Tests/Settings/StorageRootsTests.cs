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
    public void With_nothing_configured_the_folder_stays_beside_the_app()
    {
        StorageRoots.InDataFolder(new StoragePathsOptions(), _app, "video-thumbnails", "video-thumbnails")
            .Should().Be(Path.Combine(_app, "video-thumbnails"));
    }

    [Fact]
    public void The_public_resolvers_use_the_data_folder()
    {
        var paths = ReadmeCompose();

        StorageRoots.VideoThumbnails(paths).Should().Be(Path.Combine(_data, "video-thumbnails"));
        StorageRoots.Logs(paths).Should().Be(Path.Combine(_data, "logs"));
        StorageRoots.Backups(paths).Should().Be(Path.Combine(_data, "backups"));
        StorageRoots.DataProtection(paths).Should().Be(Path.Combine(_data, "dp-keys"));
    }
}
