using Vora.Application.Settings;

namespace Vora.Application.Tests.Settings;

public sealed class StorageFolderCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-storage-check-" + Guid.NewGuid().ToString("N"));

    public StorageFolderCheckTests()
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

    [Fact]
    public void A_folder_the_server_can_write_to_is_created_and_left_empty()
    {
        var folder = Path.Combine(_root, "data", "video-thumbnails");

        var problems = StorageFolderCheck.FindUnwritable([("VideoThumbnails", folder)]);

        problems.Should().BeEmpty();
        Directory.Exists(folder).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(folder).Should().BeEmpty();
    }

    [Fact]
    public void A_folder_that_cannot_be_created_is_reported_with_its_setting()
    {
        var blocker = Path.Combine(_root, "not-a-folder");
        File.WriteAllText(blocker, "");
        var folder = Path.Combine(blocker, "logs");

        var problems = StorageFolderCheck.FindUnwritable(
        [
            ("CustomArtwork", Path.Combine(_root, "data", "custom_artwork")),
            ("Logs", folder)
        ]);

        var problem = problems.Should().ContainSingle().Subject;
        problem.Setting.Should().Be("Logs");
        problem.Folder.Should().Be(folder);
        problem.Reason.Should().NotBeNullOrWhiteSpace();
    }
}
