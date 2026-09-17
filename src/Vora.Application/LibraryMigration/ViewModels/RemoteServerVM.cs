namespace Vora.Application.LibraryMigration.ViewModels;

public class RemoteServerVM
{
    public required string ClientIdentifier { get; set; }
    public required string Name { get; set; }
    public bool IsOwned { get; set; }
    public string? OwnerName { get; set; }
    public string? Platform { get; set; }
    public string? ProductVersion { get; set; }
    public bool IsOnline { get; set; }
    public required IReadOnlyList<RemoteConnectionVM> Connections { get; set; }
}
