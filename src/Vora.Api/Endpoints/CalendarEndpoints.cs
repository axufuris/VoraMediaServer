using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vora.Api.Extensions;
using Vora.Application.Calendar;

namespace Vora.Api.Endpoints;

public static class CalendarEndpoints
{
    public static RouteGroupBuilder MapCalendarEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/calendar").WithTags("Calendar").RequireAuthorization().RequireFeature(FeatureGate.ReleaseCalendar);

        group.MapGet("/", GetCalendarEventsAsync);

        return group;
    }

    private static async Task<IResult> GetCalendarEventsAsync(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        ClaimsPrincipal user,
        ICalendarManager calendarManager)
    {
        var events = await calendarManager.GetCalendarEventsAsync(
            startDate,
            endDate,
            user.HasAllLibraryAccess(),
            user.GetAllowedLibraryIds(),
            user.HasAllContentRatings(),
            user.GetAllowedMovieRatings(),
            user.GetAllowedTvRatings(),
            user.BlockUnratedContent());

        return Results.Ok(events);
    }
}
