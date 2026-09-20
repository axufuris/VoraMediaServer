namespace Vora.Application.Discovery.ViewModels;

public class DiscoveryRowConfigVM
{
    public Guid Id { get; set; }
    public string RowId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string ProviderName { get; set; } = string.Empty;
}
