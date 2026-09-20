namespace Vora.Application.Media.ViewModels;

public class DedupeIgnoredGroupVM
{
    public Guid Id { get; set; }
    public Guid MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public DateTime IgnoredAt { get; set; }
    public string? Note { get; set; }
}
