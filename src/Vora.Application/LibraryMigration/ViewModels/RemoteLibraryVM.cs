using Vora.Plugins.Dtos;

namespace Vora.Application.LibraryMigration.ViewModels;

public class RemoteLibraryVM
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required RemoteLibraryKind Kind { get; set; }
}
