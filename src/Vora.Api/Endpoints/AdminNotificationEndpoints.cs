using Microsoft.AspNetCore.Mvc;
using Vora.Application.Notifications;
using Vora.Application.Notifications.ViewModels;

namespace Vora.Api.Endpoints;

public static class AdminNotificationEndpoints
{
    public static RouteGroupBuilder MapAdminNotificationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/notifications").WithTags("Admin Notifications").RequireAuthorization("AdminOnly");

        group.MapGet("/", GetRecentAsync).WithName("ListAdminNotifications").Produces<List<AdminNotificationVM>>();
        group.MapGet("/unread-count", GetUnreadCountAsync).WithName("GetAdminNotificationUnreadCount").Produces<int>();
        group.MapPut("/{id:guid}/read", MarkReadAsync).WithName("MarkAdminNotificationRead").Produces(StatusCodes.Status204NoContent);
        group.MapPost("/mark-all-read", MarkAllReadAsync).WithName("MarkAllAdminNotificationsRead").Produces(StatusCodes.Status204NoContent);
        group.MapDelete("/", ClearAllAsync).WithName("ClearAdminNotifications").Produces(StatusCodes.Status204NoContent);

        return group;
    }

    private static async Task<IResult> GetRecentAsync([FromQuery] int? limit, [FromQuery] bool unreadOnly, IAdminNotificationManager manager)
    {
        var list = await manager.GetRecentAsync(Math.Clamp(limit ?? 50, 1, 200), unreadOnly);
        return Results.Ok(list);
    }

    private static async Task<IResult> GetUnreadCountAsync(IAdminNotificationManager manager)
    {
        var count = await manager.GetUnreadCountAsync();
        return Results.Ok(count);
    }

    private static async Task<IResult> MarkReadAsync(Guid id, IAdminNotificationManager manager)
    {
        var ok = await manager.MarkReadAsync(id);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> MarkAllReadAsync(IAdminNotificationManager manager)
    {
        await manager.MarkAllReadAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ClearAllAsync(IAdminNotificationManager manager)
    {
        await manager.ClearAllAsync();
        return Results.NoContent();
    }
}
