namespace Vora.Plugins.Dtos;

public class RemoteAccountDto
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public required RemoteAccountKind Kind { get; set; }
    public bool HasPin { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Email { get; set; }
}
