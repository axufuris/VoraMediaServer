namespace Vora.Application.Collections.Dtos;

public class CollectionChronologyConfigDto
{
    public string Title { get; set; } = string.Empty;
    public string? SortProviderId { get; set; }
    public string? ExternalListId { get; set; }
    public string? ChronologyItemsSignature { get; set; }
}
