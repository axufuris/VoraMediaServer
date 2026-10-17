using Vora.Application.Streaming;

namespace Vora.Application.Tests.Streaming;

public class BandwidthSampleTests
{
    [Theory]
    [InlineData(512, 512 * 1024)]
    [InlineData(1, BandwidthSample.MinKilobytes * 1024)]
    [InlineData(1_000_000, BandwidthSample.MaxKilobytes * 1024)]
    public async Task Writes_the_size_asked_for_within_the_limits(int kilobytes, long expectedBytes)
    {
        await using var output = new MemoryStream();

        await BandwidthSample.WriteAsync(output, kilobytes, TestContext.Current.CancellationToken);

        output.Length.Should().Be(expectedBytes);
    }

    [Fact]
    public async Task The_bytes_do_not_compress_into_a_faster_reading()
    {
        await using var output = new MemoryStream();
        await BandwidthSample.WriteAsync(output, 256, TestContext.Current.CancellationToken);

        await using var compressed = new MemoryStream();
        await using (var gzip = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            output.Position = 0;
            await output.CopyToAsync(gzip, TestContext.Current.CancellationToken);
        }

        compressed.Length.Should().BeGreaterThan(output.Length * 9 / 10);
    }
}
