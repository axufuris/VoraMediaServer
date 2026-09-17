using System.ComponentModel.DataAnnotations.Schema;

namespace Vora.Domain.Entities.Discovery;

public class DiscoveryRowConfig
{
    public Guid Id { get; set; }
    public string RowId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public bool IsEnabled { get; set; } = true;

    [NotMapped]
    public string ProviderName { get; set; } = string.Empty;
}
