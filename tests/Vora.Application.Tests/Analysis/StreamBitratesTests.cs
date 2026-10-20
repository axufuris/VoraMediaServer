using Vora.Application.Analysis;

namespace Vora.Application.Tests.Analysis;

public class StreamBitratesTests
{
    [Fact]
    public void A_stream_cannot_carry_more_than_the_whole_file()
    {
        StreamBitrates.Within(23_986_068, 3_706_601).Should().BeNull();
    }

    [Theory]
    [InlineData(29_800_000L, 35_000_000L)]
    [InlineData(35_000_000L, 35_000_000L)]
    [InlineData(8_000_000L, null)]
    [InlineData(8_000_000L, 0L)]
    public void A_stream_that_fits_inside_the_file_or_a_file_never_measured_keeps_its_bitrate(long stream, long? file)
    {
        StreamBitrates.Within(stream, file).Should().Be(stream);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void No_bitrate_stays_no_bitrate(long? stream)
    {
        StreamBitrates.Within(stream, 3_706_601).Should().BeNull();
    }
}
