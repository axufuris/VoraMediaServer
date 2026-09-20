namespace Vora.Application.LibraryMigration.ViewModels;

public class RemoteConnectionVM
{
    public required string Uri { get; set; }
    public bool IsLocal { get; set; }
    public bool IsHttps { get; set; }
    public bool IsRelay { get; set; }
}
