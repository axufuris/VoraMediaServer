namespace Vora.Plugins.Dtos;

public enum RemoteLibraryKind
{
    Movie,
    Show,
    Music,
    Other
}

public class RemoteLibraryDto
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required RemoteLibraryKind Kind { get; set; }
}
