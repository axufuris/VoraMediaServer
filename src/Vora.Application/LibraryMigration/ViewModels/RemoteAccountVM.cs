using Vora.Plugins.Dtos;

namespace Vora.Application.LibraryMigration.ViewModels;

public class RemoteAccountVM
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public required RemoteAccountKind Kind { get; set; }
    public bool HasPin { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Email { get; set; }
}
