namespace Vora.Application.Discovery.Requests;

public class DiscoveryRowConfigRequest
{
    public string RowId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public bool IsEnabled { get; set; }
}
