namespace Vora.Application.Requests.Dtos;

public class SaveRequestServerDto
{
    public string Name { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool UseSsl { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string UrlBase { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool Is4K { get; set; }
    public string ProviderSettingsJson { get; set; } = "{}";
    public bool IsEnabled { get; set; }
}
