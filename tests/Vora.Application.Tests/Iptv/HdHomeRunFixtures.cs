namespace Vora.Application.Tests.Iptv;

/// <summary>What an HDHomeRun serves: the documented fields, plus the extra ones real firmware adds.</summary>
internal static class HdHomeRunFixtures
{
    public const string Address = "192.168.1.50";

    public const string Discover = """
        {"FriendlyName":"HDHomeRun FLEX 4K","ModelNumber":"HDHR5-4K","FirmwareName":"hdhomerun5_atsc","FirmwareVersion":"20240101",
         "DeviceID":"1080ABCD","DeviceAuth":"secret-auth","BaseURL":"http://192.168.1.50:80","LineupURL":"http://192.168.1.50:80/lineup.json","TunerCount":4}
        """;

    public const string Lineup = """
        [
         {"GuideNumber":"2.1","GuideName":"WCBS-HD","VideoCodec":"MPEG2","AudioCodec":"AC3","HD":1,"URL":"http://192.168.1.50:5004/auto/v2.1"},
         {"GuideNumber":"7.1","GuideName":"WABC-HD","HD":1,"Tags":"favorite","URL":"http://192.168.1.50:5004/auto/v7.1"},
         {"GuideNumber":"7.2","GuideName":"Locked","Tags":"drm,favorite","URL":"http://192.168.1.50:5004/auto/v7.2"},
         {"GuideNumber":"9.1","GuideName":"Old DRM flag","DRM":1,"URL":"http://192.168.1.50:5004/auto/v9.1"},
         {"GuideNumber":"11.1","URL":"http://0.0.0.0:5004/auto/v11.1"},
         {"GuideNumber":"2.1","GuideName":"Repeated","URL":"http://192.168.1.50:5004/auto/v2.1"},
         {"GuideName":"No number","URL":"http://192.168.1.50:5004/auto/v99"}
        ]
        """;
}
