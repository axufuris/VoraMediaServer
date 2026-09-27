using Vora.Domain.Enums;

namespace Vora.Application.Media;

public class MediaExtraVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public MediaExtraType ExtraType { get; set; }
    public string? Container { get; set; }
}
