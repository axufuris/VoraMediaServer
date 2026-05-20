using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Settings;

namespace Vora.Api.Extensions;

public enum FeatureGate
{
    Discover,
    ForYou,
    ReleaseCalendar,
    LiveTv,
    Dvr,
    InternetRadio,
    Podcasts
}

public class RequireFeatureFilter : IEndpointFilter
{
    private readonly FeatureGate _feature;

    public RequireFeatureFilter(FeatureGate feature)
    {
        _feature = feature;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.User.IsAdmin())
        {
            return await next(context);
        }

        var settingsRepo = context.HttpContext.RequestServices.GetRequiredService<ISystemSettingsRepository>();
        var settings = await settingsRepo.GetSettingsAsync();

        bool enabled = _feature switch
        {
            FeatureGate.Discover => settings.EnableDiscover,
            FeatureGate.ForYou => settings.EnableForYou,
            FeatureGate.ReleaseCalendar => settings.EnableReleaseCalendar,
            FeatureGate.LiveTv => settings.EnableLiveTv,
            FeatureGate.Dvr => settings.EnableLiveTv && settings.EnableDvr,
            FeatureGate.InternetRadio => settings.EnableInternetRadio,
            FeatureGate.Podcasts => settings.EnablePodcasts,
            _ => true
        };

        if (!enabled) return Results.StatusCode(StatusCodes.Status403Forbidden);
        return await next(context);
    }
}

public static class FeatureGateExtensions
{
    public static RouteGroupBuilder RequireFeature(this RouteGroupBuilder group, FeatureGate feature)
    {
        return group.AddEndpointFilter(new RequireFeatureFilter(feature));
    }

    public static RouteHandlerBuilder RequireFeature(this RouteHandlerBuilder route, FeatureGate feature)
    {
        return route.AddEndpointFilter(new RequireFeatureFilter(feature));
    }
}
