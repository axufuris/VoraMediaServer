using System.Linq.Expressions;
using Vora.Domain.Entities.Requests;

namespace Vora.Application.Requests.ViewModels;

public class RequestServerVM
{
    public Guid Id { get; set; }
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
    public bool IsEnabled { get; set; } = true;

    public static Expression<Func<RequestServer, RequestServerVM>> Projection =>
        s => new RequestServerVM
        {
            Id = s.Id,
            Name = s.Name,
            ProviderId = s.ProviderId,
            MediaType = s.MediaType,
            Hostname = s.Hostname,
            Port = s.Port,
            UseSsl = s.UseSsl,
            ApiKey = s.ApiKey,
            UrlBase = s.UrlBase,
            IsDefault = s.IsDefault,
            Is4K = s.Is4K,
            ProviderSettingsJson = s.ProviderSettingsJson,
            IsEnabled = s.IsEnabled
        };

    public static RequestServerVM FromEntity(RequestServer s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        ProviderId = s.ProviderId,
        MediaType = s.MediaType,
        Hostname = s.Hostname,
        Port = s.Port,
        UseSsl = s.UseSsl,
        ApiKey = s.ApiKey,
        UrlBase = s.UrlBase,
        IsDefault = s.IsDefault,
        Is4K = s.Is4K,
        ProviderSettingsJson = s.ProviderSettingsJson,
        IsEnabled = s.IsEnabled
    };
}
