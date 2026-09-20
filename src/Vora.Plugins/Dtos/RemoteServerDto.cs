namespace Vora.Plugins.Dtos;

public class RemoteServerDto
{
    public required string ClientIdentifier { get; set; }
    public required string Name { get; set; }
    public bool IsOwned { get; set; }
    public string? OwnerName { get; set; }
    public string? Platform { get; set; }
    public string? ProductVersion { get; set; }
    public bool IsOnline { get; set; }
    public required IReadOnlyList<RemoteConnectionDto> Connections { get; set; }
}
