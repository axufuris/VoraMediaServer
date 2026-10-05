using System.Text;
using Vora.Application.Subtitles;

namespace Vora.Application.Tests.Subtitles;

public sealed class SubtitleFileContentTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vora-sub-content-" + Guid.NewGuid().ToString("N"));

    public SubtitleFileContentTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void A_zero_byte_file_has_no_text()
    {
        SubtitleFileContent.HasText(Write("empty.srt", [])).Should().BeFalse();
    }

    [Fact]
    public void A_file_of_byte_order_marks_blank_lines_and_padding_has_no_text()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, 0x0D, 0x0A, 0x20, 0x09, 0x0D, 0x0A, 0x00, 0x00 };

        SubtitleFileContent.HasText(Write("blank.srt", bytes)).Should().BeFalse();
    }

    [Fact]
    public void A_real_subtitle_has_text()
    {
        var bytes = Encoding.UTF8.GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\nHello\r\n");

        SubtitleFileContent.HasText(Write("movie.en.srt", bytes)).Should().BeTrue();
    }

    [Fact]
    public void A_utf16_subtitle_has_text()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("1\r\n00:00:01,000 --> 00:00:02,000\r\nHello\r\n")).ToArray();

        SubtitleFileContent.HasText(Write("movie.en.srt", bytes)).Should().BeTrue();
    }

    [Fact]
    public void A_missing_file_has_no_text()
    {
        SubtitleFileContent.HasText(Path.Combine(_folder, "gone.srt")).Should().BeFalse();
    }
}
