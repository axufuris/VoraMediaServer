namespace Vora.Application.Tests.Iptv;

public static class XtreamFixtures
{
    public const string Server = "http://provider.example.com:8080";
    public const string Username = "viewer";
    public const string Password = "example-password";

    public const string ActiveAccount = """
        {
          "user_info": {
            "username": "viewer",
            "password": "example-password",
            "message": "Welcome",
            "auth": 1,
            "status": "Active",
            "exp_date": "4102444800",
            "is_trial": "0",
            "active_cons": "0",
            "created_at": "1700000000",
            "max_connections": "2",
            "allowed_output_formats": ["m3u8", "ts", "rtmp"]
          },
          "server_info": {
            "url": "provider.example.com",
            "port": "8080",
            "https_port": "8443",
            "server_protocol": "http",
            "timezone": "Europe/London",
            "timestamp_now": 1762900000
          }
        }
        """;

    public const string TransportStreamOnlyAccount = """
        {
          "user_info": {
            "auth": 1,
            "status": "Active",
            "exp_date": null,
            "max_connections": 1,
            "allowed_output_formats": ["ts"]
          }
        }
        """;

    public const string RejectedAccount = """
        {"user_info":{"auth":0}}
        """;

    public const string ExpiredStatusAccount = """
        {
          "user_info": {
            "auth": 1,
            "status": "Expired",
            "exp_date": "1600000000",
            "max_connections": "1",
            "allowed_output_formats": ["m3u8", "ts"]
          }
        }
        """;

    public const string LapsedAccount = """
        {
          "user_info": {
            "auth": "1",
            "status": "Active",
            "exp_date": "1600000000",
            "max_connections": "1",
            "allowed_output_formats": ["ts"]
          }
        }
        """;

    public const string Categories = """
        [
          {"category_id": "1", "category_name": "UK | Entertainment", "parent_id": 0},
          {"category_id": "2", "category_name": "News ", "parent_id": 0},
          {"category_id": 3, "category_name": "Radio", "parent_id": 0}
        ]
        """;

    public const string LiveStreams = """
        [
          {"num": 1, "name": "UK: BBC One FHD", "stream_type": "live", "stream_id": 1002, "stream_icon": "", "epg_channel_id": "BBC1.uk", "category_id": "1"},
          {"num": 2, "name": "UK: BBC One HD", "stream_type": "live", "stream_id": 1001, "stream_icon": "http://logos.example.com/bbc1.png", "epg_channel_id": "bbc1.uk", "category_id": "1"},
          {"num": 3, "name": "Sky News", "stream_type": "live", "stream_id": "1003", "stream_icon": "not-a-url", "epg_channel_id": null, "category_id": "2"},
          {"num": 4, "name": "Jazz FM", "stream_type": "radio_streams", "stream_id": 1004, "epg_channel_id": "", "category_ids": [3]},
          {"num": 5, "name": "Broken", "stream_type": "live", "stream_id": "../../etc", "epg_channel_id": "broken"},
          {"num": 6, "name": "No id", "stream_type": "live"}
        ]
        """;
}
