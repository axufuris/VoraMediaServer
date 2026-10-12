using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Settings;
using Vora.Application.Thumbnails;

namespace Vora.Application.Tests.Thumbnails;

public sealed class VideoThumbnailStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-thumbnail-storage-" + Guid.NewGuid().ToString("N"));
    private readonly ILogger<VideoThumbnailStorageService> _logger = Substitute.For<ILogger<VideoThumbnailStorageService>>();

    public VideoThumbnailStorageServiceTests()
    {
        Directory.CreateDirectory(_root);
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

    private VideoThumbnailStorageService Service(StoragePathsOptions paths) => new(Options.Create(paths), _logger);

    [Fact]
    public void The_configured_folder_is_created()
    {
        var folder = Path.Combine(_root, "data", "video-thumbnails");

        var service = Service(new StoragePathsOptions { VideoThumbnails = folder });

        service.RootDirectory.Should().Be(folder);
        Directory.Exists(folder).Should().BeTrue();
    }

    [Fact]
    public void Left_unset_the_folder_sits_beside_the_other_data_folders()
    {
        var service = Service(new StoragePathsOptions { CustomArtwork = Path.Combine(_root, "data", "custom_artwork") });

        service.RootDirectory.Should().Be(Path.Combine(_root, "data", "video-thumbnails"));
        Directory.Exists(service.RootDirectory).Should().BeTrue();
    }

    [Fact]
    public void A_folder_that_cannot_be_created_is_logged_and_does_not_stop_the_service_being_built()
    {
        var blocker = Path.Combine(_root, "not-a-folder");
        File.WriteAllText(blocker, "");
        var folder = Path.Combine(blocker, "video-thumbnails");

        var service = Service(new StoragePathsOptions { VideoThumbnails = folder });

        service.RootDirectory.Should().Be(folder);
        service.HasGeneratedAssets(Guid.NewGuid()).Should().BeFalse();
        _logger.ReceivedCalls().Should().ContainSingle(call => (LogLevel)call.GetArguments()[0]! == LogLevel.Error);
    }
}
