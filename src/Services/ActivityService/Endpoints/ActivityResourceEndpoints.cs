using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

public static class ActivityResourceEndpoints
{
    public static void MapActivityResourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = SafeGroup(app, "/api/activities/{activityId:int}/resources").WithTags("Activity resources");
        group.MapGet("", Read);
        group.MapGet("/history", History);
        group.MapPost("", Create);
        group.MapPatch("/{resourceId:guid}", Patch);
        group.MapDelete("/{resourceId:guid}", Detach);
        group.MapPost("/upload-intents", UploadUnavailable);
    }

    internal sealed record ResourceScope(ClubActivity Activity, bool Manager, bool CanReadClub);
    internal static async Task<ResourceScope> Access(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await db.Activities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == activityId, ct);
        Require(activity is not null, 404, "ACTIVITY_NOT_FOUND", "The activity does not exist.");
        var scope = await Authorize(activity!.ClubId, http, ct);
        var canReadClub = scope.Manager || scope.Club.IsApprovedMember;
        var visible = canReadClub || scope.Reviewer
            || activity.DiscoveryVisibility == "CAMPUS_PUBLIC" && activity.PublicationStatus is "PUBLISHED" or "CLOSED";
        Require(visible && (activity.PublicationStatus != "DRAFT" || scope.Manager || scope.Reviewer),
            403, "RESOURCE_ACCESS_DENIED", "Current activity access is required.");
        return new(activity, scope.Manager, canReadClub);
    }
    private static void Management(ResourceScope scope) => Require(scope.Manager, 403, "RESOURCE_MANAGEMENT_REQUIRED", "Current club management is required.");
    private static async Task Writable(ResourceScope scope, ActivityDbContext db, CancellationToken ct) =>
        Require(await db.Activities.AnyAsync(x => x.Id == scope.Activity.Id && x.Status != ActivityStatuses.Cancelled, ct),
            409, "ACTIVITY_CANCELLED", "Resources of a cancelled activity are read-only.");
    internal static ActivityResourceCapabilities Capabilities(ResourceScope scope) => new(scope.Manager && scope.Activity.Status != ActivityStatuses.Cancelled, false, ["EMBED"]);

    internal static async Task<ActivityResourceGallery> Gallery(ResourceScope scope, ActivityDbContext db, CancellationToken ct)
    {
        var rows = await db.Set<ActivityResource>().AsNoTracking().Where(x => x.ActivityId == scope.Activity.Id && x.DetachedAtUtc == null
            && (scope.CanReadClub || x.Visibility == "PUBLIC"))
            .OrderBy(x => x.Position).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
        return new(rows.Select(x => Response(x, scope.Manager)).ToArray(), Capabilities(scope));
    }
    private static async Task<IResult> Read(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct) =>
        Results.Ok(await Gallery(await Access(activityId, db, http, ct), db, ct));

    private static async Task<IResult> Create(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Access(activityId, db, http, ct); Management(scope);
        var input = await Body<ActivityResourceCreate>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await Writable(scope, db, ct);
            if (input.Kind is "DOCUMENT" or "MEDIA") return FilesUnavailable();
            Require(input.Kind == "EMBED" && input.UploadIntentId is null, 400, "INVALID_RESOURCE_KIND", "Use EMBED without an upload intent.");
            var title = Title(input.Title); var visibility = Visibility(input.Visibility ?? "CLUB");
            var (url, provider) = ActivityResourceLinkPolicy.Normalize(input.Url);
            var existing = await db.Set<ActivityResource>().Where(x => x.ActivityId == activityId && x.DetachedAtUtc == null).ToListAsync(ct);
            Require(existing.Count < 100, 409, "RESOURCE_LIMIT_REACHED", "An activity supports at most 100 attached resources.");
            var position = input.Position ?? (existing.Count == 0 ? 0 : Math.Min(9999, existing.Max(x => x.Position) + 1));
            Position(position);
            var row = new ActivityResource
            {
                ActivityId = activityId,
                Title = title,
                Visibility = visibility,
                Url = url,
                Provider = provider,
                Position = position,
                UploadedByUserId = http.User.GetUserId(),
                CreatedAtUtc = Now(http),
                UpdatedAtUtc = Now(http)
            };
            db.Add(row); Audit(db, row, "CREATE", null, null, http); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/activities/{activityId}/resources/{row.Id}", Response(row, true));
        }, ct);
    }
    private static async Task<IResult> Patch(int activityId, Guid resourceId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Access(activityId, db, http, ct); Management(scope);
        var input = await Body<ActivityResourcePatch>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await Writable(scope, db, ct); var row = await Find(activityId, resourceId, db, ct); ExpectedVersion(input.ExpectedVersion, row);
            Require(row.DetachedAtUtc is null, 409, "RESOURCE_DETACHED", "This resource was detached.");
            Require(input.Title.ValueKind != JsonValueKind.Undefined || input.Visibility.ValueKind != JsonValueKind.Undefined || input.Position.ValueKind != JsonValueKind.Undefined,
                400, "EMPTY_PATCH", "Provide at least one resource field.");
            var before = Snapshot(row);
            if (input.Title.ValueKind != JsonValueKind.Undefined)
            { Require(input.Title.ValueKind == JsonValueKind.String, 400, "INVALID_RESOURCE_TITLE", "Title must be a string."); row.Title = Title(input.Title.GetString()); }
            if (input.Visibility.ValueKind != JsonValueKind.Undefined)
            { Require(input.Visibility.ValueKind == JsonValueKind.String, 400, "INVALID_RESOURCE_VISIBILITY", "Visibility must be a string."); row.Visibility = Visibility(input.Visibility.GetString()); }
            if (input.Position.ValueKind != JsonValueKind.Undefined)
            { Require(input.Position.ValueKind == JsonValueKind.Number && input.Position.TryGetInt32(out _), 400, "INVALID_RESOURCE_POSITION", "Position must be an integer."); row.Position = input.Position.GetInt32(); Position(row.Position); }
            row.Version = Guid.NewGuid(); row.UpdatedAtUtc = Now(http); Audit(db, row, "UPDATE", before, null, http);
            await db.SaveChangesAsync(ct); return Results.Ok(Response(row, true));
        }, ct);
    }
    private static async Task<IResult> Detach(int activityId, Guid resourceId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Access(activityId, db, http, ct); Management(scope);
        var input = await Body<ActivityResourceDetach>(http, ct);
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            await Writable(scope, db, ct); var row = await Find(activityId, resourceId, db, ct); ExpectedVersion(input.ExpectedVersion, row);
            Require(row.DetachedAtUtc is null, 409, "RESOURCE_DETACHED", "This resource was detached.");
            var reason = input.Reason?.Trim(); Require(reason is { Length: > 0 and <= 500 }, 400, "REASON_REQUIRED", "Provide a reason of 1–500 characters.");
            var before = Snapshot(row); row.DetachedAtUtc = Now(http); row.UpdatedAtUtc = Now(http); row.Version = Guid.NewGuid();
            Audit(db, row, "DETACH", before, reason, http); await db.SaveChangesAsync(ct);
            return Results.Ok(new { row.Id, row.Version, row.DetachedAtUtc });
        }, ct);
    }
    private static async Task<IResult> History(int activityId, int? page, int? pageSize, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Access(activityId, db, http, ct); Management(scope);
        var number = page ?? 1; var size = pageSize ?? 50;
        Require(number >= 1 && size is >= 1 and <= 100, 400, "INVALID_PAGING", "Use page >= 1 and pageSize 1–100.");
        var query = db.Set<ActivityResourceAudit>().AsNoTracking().Where(x => x.ActivityId == activityId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Id).Skip((int)Math.Min((long)(number - 1) * size, int.MaxValue)).Take(size).ToListAsync(ct);
        return Results.Ok(new { items = rows, page = number, pageSize = size, total });
    }
    private static async Task<IResult> UploadUnavailable(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    { Management(await Access(activityId, db, http, ct)); return FilesUnavailable(); }
    private static IResult FilesUnavailable() => ApiOperations.Error(501, "RESOURCE_UPLOAD_NOT_IMPLEMENTED", "Document/media uploads, scanning and private delivery are not implemented. Use supported EMBED links.");
    private static async Task<ActivityResource> Find(int activityId, Guid resourceId, ActivityDbContext db, CancellationToken ct)
    {
        var row = await db.Set<ActivityResource>().SingleOrDefaultAsync(x => x.ActivityId == activityId && x.Id == resourceId, ct);
        Require(row is not null, 404, "RESOURCE_NOT_FOUND", "The resource does not belong to this activity."); return row!;
    }
    private static void ExpectedVersion(Guid version, ActivityResource row) => Require(version != Guid.Empty && version == row.Version,
        409, "STALE_RESOURCE", "Reload the current resource version.");
    private static string Title(string? input)
    { var title = input?.Trim(); Require(title is { Length: > 0 and <= 160 } && !title.Any(char.IsControl), 400, "INVALID_RESOURCE_TITLE", "Provide a title of 1–160 characters without control characters."); return title!; }
    private static string Visibility(string? input)
    { Require(input is "PUBLIC" or "CLUB", 400, "INVALID_RESOURCE_VISIBILITY", "Visibility must be PUBLIC or CLUB."); return input!; }
    private static void Position(int input) => Require(input is >= 0 and <= 9999, 400, "INVALID_RESOURCE_POSITION", "Position must be 0–9999.");
    private static string Snapshot(ActivityResource row) => JsonSerializer.Serialize(row, Json);
    private static void Audit(ActivityDbContext db, ActivityResource row, string action, string? before, string? reason, HttpContext http) =>
        db.Add(new ActivityResourceAudit
        {
            ActivityId = row.ActivityId,
            ResourceId = row.Id,
            Action = action,
            ActorId = http.User.GetUserId(),
            BeforeJson = before,
            AfterJson = Snapshot(row),
            Reason = reason,
            CreatedAtUtc = Now(http)
        });
    private static ActivityResourceResponse Response(ActivityResource row, bool manager) => new(row.Id, row.Kind, row.Title, row.Visibility,
        row.Url, row.Provider, row.Position, row.Version, row.CreatedAtUtc, row.UpdatedAtUtc)
    { UploadedByUserId = manager ? row.UploadedByUserId : null };
}
