using Vora.Application.Iptv;

namespace Vora.Application.Tests.Iptv;

public class DvrRecordingArgumentsTests
{
    private const string StreamUrl = "http://provider.example.com:8080/live/viewer/example-password/1001.ts";

    [Fact]
    public void A_recording_of_a_free_channel_reads_the_stream_as_before()
    {
        var info = DvrRecordingService.BuildRecordingProcessInfo("https://iptv.example.com/news.ts", IptvRequestPolicy.None);

        info.ArgumentList.Should().Equal(
            "-reconnect", "1",
            "-reconnect_streamed", "1",
            "-reconnect_delay_max", "5",
            "-i", "https://iptv.example.com/news.ts",
            "-fflags", "+genpts",
            "-c", "copy",
            "-f", "mpegts",
            "pipe:1");
    }

    [Fact]
    public void A_recording_sends_the_provider_s_user_agent_and_referer()
    {
        var policy = new IptvRequestPolicy("IPTVSmartersPro", "https://portal.example.com/", null);

        var info = DvrRecordingService.BuildRecordingProcessInfo(StreamUrl, policy);

        info.ArgumentList.Should().Equal(
            "-reconnect", "1",
            "-reconnect_streamed", "1",
            "-reconnect_delay_max", "5",
            "-user_agent", "IPTVSmartersPro",
            "-headers", "Referer: https://portal.example.com/\r\n",
            "-i", StreamUrl,
            "-fflags", "+genpts",
            "-c", "copy",
            "-f", "mpegts",
            "pipe:1");
    }
}
