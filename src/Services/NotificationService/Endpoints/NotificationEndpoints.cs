using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using NotificationService.Data;
using NotificationService.Extensions;
using NotificationService.Models;
using System.Security.Claims;

namespace NotificationService.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var notifications = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization(AuthPolicies.AllActors);

        notifications.MapGet("/", GetNotificationsAsync);
        notifications.MapGet("/{id:int}", GetNotificationAsync);
        notifications.MapPut("/{id:int}/read", MarkAsReadAsync);
        notifications.MapPut("/read-all", MarkAllAsReadAsync);
    }

    private static async Task<IResult> GetNotificationAsync(
        int id,
        NotificationDbContext db,
        ClaimsPrincipal user,
        ClubAccessClient clubAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (notification is null)
        {
            return Results.NotFound();
        }

        var recipientRoles = await NotificationExtensions.GetRecipientRolesAsync(user, clubAccess, httpContext, cancellationToken);
        if (!NotificationExtensions.CanAccessNotification(user, recipientRoles, notification))
        {
            return Results.Forbid();
        }

        var actorId = user.GetUserId();
        var isRead = notification.RecipientUserId == actorId && notification.IsRead
            || await db.ReadReceipts.AnyAsync(x => x.NotificationId == id && x.UserId == actorId, cancellationToken);
        return Results.Ok(NotificationExtensions.ToResponse(notification, isRead));
    }

    private static async Task<IResult> GetNotificationsAsync(
        int? recipientUserId,
        string? recipientRole,
        bool? unreadOnly,
        NotificationDbContext db,
        ClaimsPrincipal user,
        ClubAccessClient clubAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var recipientRoles = await NotificationExtensions.GetRecipientRolesAsync(user, clubAccess, httpContext, cancellationToken);
        var query = NotificationExtensions.ScopeNotificationQuery(db.Notifications.AsQueryable(), user, recipientRoles, recipientUserId, recipientRole);
        if (query is null)
        {
            return Results.Forbid();
        }

        var actorId = user.GetUserId();
        if (unreadOnly == true)
        {
            query = UnreadForActor(query, db, actorId);
        }

        var rows = await query.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(cancellationToken);
        var ids = rows.Select(x => x.Id).ToArray();
        int[] readIds = ids.Length == 0 ? [] : await db.ReadReceipts.AsNoTracking()
            .Where(x => x.UserId == actorId && ids.Contains(x.NotificationId))
            .Select(x => x.NotificationId).ToArrayAsync(cancellationToken);
        var readSet = readIds.ToHashSet();
        return Results.Ok(rows.Select(x => NotificationExtensions.ToResponse(x,
            readSet.Contains(x.Id) || x.RecipientUserId == actorId && x.IsRead)));
    }

    private static async Task<IResult> MarkAsReadAsync(
        int id,
        NotificationDbContext db,
        ClaimsPrincipal user,
        ClubAccessClient clubAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (notification is null)
        {
            return Results.NotFound();
        }

        var recipientRoles = await NotificationExtensions.GetRecipientRolesAsync(user, clubAccess, httpContext, cancellationToken);
        if (!NotificationExtensions.CanAccessNotification(user, recipientRoles, notification))
        {
            return Results.Forbid();
        }

        await SaveReadReceiptsAsync(db.Notifications.Where(x => x.Id == id), db, user.GetUserId(), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllAsReadAsync(
        int? recipientUserId,
        string? recipientRole,
        NotificationDbContext db,
        ClaimsPrincipal user,
        ClubAccessClient clubAccess,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var recipientRoles = await NotificationExtensions.GetRecipientRolesAsync(user, clubAccess, httpContext, cancellationToken);
        var query = NotificationExtensions.ScopeNotificationQuery(db.Notifications, user, recipientRoles, recipientUserId, recipientRole);
        if (query is null)
        {
            return Results.Forbid();
        }

        await SaveReadReceiptsAsync(query, db, user.GetUserId(), cancellationToken);
        return Results.NoContent();
    }

    private static IQueryable<Notification> UnreadForActor(IQueryable<Notification> query, NotificationDbContext db, int actorId)
        => query.Where(x => !(x.RecipientUserId == actorId && x.IsRead)
            && !db.ReadReceipts.Any(receipt => receipt.NotificationId == x.Id && receipt.UserId == actorId));

    private static async Task SaveReadReceiptsAsync(IQueryable<Notification> query, NotificationDbContext db,
        int actorId, CancellationToken cancellationToken)
    {
        var lastId = await query.MaxAsync(x => (int?)x.Id, cancellationToken);
        if (lastId is null) return;
        query = query.Where(x => x.Id <= lastId.Value);

        // Bounded batches keep read-all from materializing an entire notification history.
        // A concurrent mark may win a receipt insert; re-query the remaining rows after that conflict.
        while (true)
        {
            var ids = await UnreadForActor(query, db, actorId).OrderBy(x => x.Id)
                .Select(x => x.Id).Take(250).ToArrayAsync(cancellationToken);
            if (ids.Length == 0) return;

            var readAt = DateTimeOffset.UtcNow;
            var receipts = ids.Select(id => new NotificationReadReceipt
            {
                NotificationId = id,
                UserId = actorId,
                ReadAtUtc = readAt
            }).ToArray();
            db.ReadReceipts.AddRange(receipts);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // Another mark operation inserted an overlapping composite key.
            }
            finally
            {
                foreach (var receipt in receipts) db.Entry(receipt).State = EntityState.Detached;
            }
        }
    }
}
