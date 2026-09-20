namespace Vora.Plugins.Dtos;

public class RemoteConnectionDto
{
    public required string Uri { get; set; }
    public bool IsLocal { get; set; }
    public bool IsHttps { get; set; }
    public bool IsRelay { get; set; }
}
