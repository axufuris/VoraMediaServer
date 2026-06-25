using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Users;

namespace Vora.Api.Extensions;

public sealed class AccountOwnershipFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        var accountId = http.User.GetAccountId();
        if (accountId == null) return Results.Unauthorized();

        if (http.User.IsAdmin()) return await next(context);

        var route = http.Request.RouteValues;

        if (route.TryGetValue("userId", out var rawUserId)
            && Guid.TryParse(rawUserId?.ToString(), out var userId)
            && userId != accountId.Value)
        {
            return Results.Forbid();
        }

        if (route.TryGetValue("profileId", out var rawProfileId)
            && Guid.TryParse(rawProfileId?.ToString(), out var profileId))
        {
            var manager = http.RequestServices.GetRequiredService<IUserManager>();
            if (!await manager.AccountOwnsProfileAsync(accountId.Value, profileId))
            {
                return Results.Forbid();
            }
        }

        return await next(context);
    }
}
