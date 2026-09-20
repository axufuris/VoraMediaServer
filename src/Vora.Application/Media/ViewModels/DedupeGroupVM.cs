namespace Vora.Application.Media.ViewModels;

public class DedupeGroupVM
{
    public Guid MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string MediaKind { get; set; } = "video";
    public string Resolution { get; set; } = string.Empty;
    public List<DedupeItemVM> Parts { get; set; } = new();
}
