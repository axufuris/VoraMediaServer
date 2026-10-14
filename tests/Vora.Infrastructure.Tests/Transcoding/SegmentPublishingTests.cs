using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public sealed class SegmentPublishingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vora-segment-publishing-" + Guid.NewGuid().ToString("N"));

    public SegmentPublishingTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string ListPath => Path.Combine(_directory, "segments.list");

    private string File(string name, string content = "data")
    {
        var path = Path.Combine(_directory, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    private IEnumerable<string> Names() => Directory.EnumerateFiles(_directory).Select(Path.GetFileName).OfType<string>().Order();

    [Fact]
    public void A_segment_ffmpeg_has_listed_is_published_and_the_one_still_being_written_is_not()
    {
        File("key_4.part");
        File("key_5.part");
        File("segments.list", "key_4.part\n");

        FFmpegTranscodeService.PublishFinishedSegments(_directory, ListPath);

        Names().Should().Equal("key_4.ts", "key_5.part", "segments.list");
    }

    [Fact]
    public void A_list_line_still_being_written_does_not_publish_its_segment()
    {
        File("key_4.part");
        File("key_5.part");
        File("segments.list", "key_4.part\nkey_5.pa");

        FFmpegTranscodeService.PublishFinishedSegments(_directory, ListPath);

        Names().Should().Equal("key_4.ts", "key_5.part", "segments.list");
    }

    [Fact]
    public void Publishing_replaces_the_same_segment_from_an_earlier_run()
    {
        File("key_4.ts", "earlier");
        File("key_4.part", "later");
        File("segments.list", "key_4.part\n");

        FFmpegTranscodeService.PublishFinishedSegments(_directory, ListPath);

        System.IO.File.ReadAllText(Path.Combine(_directory, "key_4.ts")).Should().Be("later");
        Names().Should().Equal("key_4.ts", "segments.list");
    }

    [Fact]
    public void A_listed_segment_already_published_or_missing_is_skipped()
    {
        File("key_4.ts");
        File("segments.list", "key_4.part\nkey_5.part\n");

        FFmpegTranscodeService.PublishFinishedSegments(_directory, ListPath);

        Names().Should().Equal("key_4.ts", "segments.list");
    }

    [Fact]
    public void No_list_yet_publishes_nothing()
    {
        File("key_0.part");

        FFmpegTranscodeService.PublishFinishedSegments(_directory, ListPath);

        Names().Should().Equal("key_0.part");
    }
}
