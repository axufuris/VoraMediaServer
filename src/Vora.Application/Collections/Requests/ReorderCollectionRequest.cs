namespace Vora.Application.Collections.Requests;

public class ReorderCollectionRequest
{
    public required List<Guid> MediaItemIds { get; set; }
}
