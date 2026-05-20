using System.Linq.Expressions;
using Vora.Domain.Entities.Settings;

namespace Vora.Application.Settings.Dtos;

public class RemoteAccessSettingsDto
{
    public bool EnableRemoteAccess { get; set; }
    public bool ManuallySpecifyPublicPort { get; set; }
    public int PublicPort { get; set; }

    public static Expression<Func<ServerSetting, RemoteAccessSettingsDto>> Projection =>
        s => new RemoteAccessSettingsDto
        {
            EnableRemoteAccess = s.EnableRemoteAccess,
            ManuallySpecifyPublicPort = s.ManuallySpecifyPublicPort,
            PublicPort = s.PublicPort
        };
}
