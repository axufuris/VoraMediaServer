using Vora.Application.Settings;
using Vora.Application.Settings.ViewModels;

namespace Vora.Api.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/system").WithTags("System");

        group.MapGet("/version", GetVersion)
            .RequireAuthorization()
            .WithName("GetServerVersion")
            .Produces<ServerVersionVM>(StatusCodes.Status200OK);

        return routes;
    }

    private static IResult GetVersion() =>
        Results.Ok(ServerVersion.Read(typeof(SystemEndpoints).Assembly));
}
