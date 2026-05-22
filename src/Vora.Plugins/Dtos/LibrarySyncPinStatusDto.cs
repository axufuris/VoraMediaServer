namespace Vora.Plugins.Dtos;

public class LibrarySyncPinStatusDto
{
    public required string PinId { get; set; }
    public required LibrarySyncPinStatus Status { get; set; }
    public LibrarySyncTokenDto? Token { get; set; }
}
