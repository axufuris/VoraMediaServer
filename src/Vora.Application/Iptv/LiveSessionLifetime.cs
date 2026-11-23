using Vora.Domain.Entities.Settings;

namespace Vora.Application.Iptv;

public static class LiveSessionLifetime
{
    private const int MinHours = 1;
    private const int MaxHours = 48;
    private const int DefaultHours = 6;
    private const double JoinableShare = 0.75;

    public static TimeSpan Of(ServerSetting settings) =>
        TimeSpan.FromHours(settings.TimeshiftMaxSessionHours <= 0 ? DefaultHours : Math.Clamp(settings.TimeshiftMaxSessionHours, MinHours, MaxHours));

    public static TimeSpan JoinableFor(ServerSetting settings) => Of(settings) * JoinableShare;
}
