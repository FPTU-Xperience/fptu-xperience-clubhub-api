using System.Buffers.Binary;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityActionHelpers;

namespace ActivityService.Endpoints;

internal static class ActivityCertificateEndpoints
{
    internal static void MapActivityCertificateEndpoints(this IEndpointRouteBuilder app)
    {
        var routes = SafeGroup(app, "/api/activities").WithTags("Activity certificates");
        routes.MapGet("/{activityId:int}/certificate-definitions", Definitions);
        routes.MapPost("/{activityId:int}/certificate-definitions", AddDefinition);
        routes.MapDelete("/{activityId:int}/certificate-definitions/{id:guid}", RemoveDefinition);
        routes.MapGet("/certificates/me", Mine);
        routes.MapGet("/certificates/me/display", GetDisplay);
        routes.MapPut("/certificates/me/display", SaveDisplay);
        routes.MapGet("/certificates/club/{clubId:int}", ForClub);
    }

    internal static async Task IssueAsync(ClubActivity activity, DateTimeOffset issuedAt, ActivityDbContext db, CancellationToken ct)
    {
        var definitions = await db.Set<ActivityCertificateDefinition>().Where(x => x.ActivityId == activity.Id).ToListAsync(ct);
        if (definitions.Count == 0) return;
        var roles = await db.Set<ActivityFinalRole>().AsNoTracking().Where(x => x.ActivityId == activity.Id).ToListAsync(ct);
        var byRole = definitions.ToLookup(x => x.Role);
        foreach (var role in roles)
            foreach (var definition in byRole[role.Role])
                db.Add(Certificate(activity, definition.Id, "STUDENT", role.UserId, role.DisplayName, issuedAt));
        if (activity.ClubId > 0 && await db.Set<ActivityHandover>().AnyAsync(x => x.ActivityId == activity.Id && x.Status == "ACCEPTED", ct))
            foreach (var definition in byRole["HOST_CLUB"])
                db.Add(Certificate(activity, definition.Id, "CLUB", activity.ClubId, activity.ClubName, issuedAt));
    }

    private static ActivityCertificate Certificate(ClubActivity activity, Guid definitionId, string kind,
        int recipientId, string name, DateTimeOffset issuedAt) => new()
    {
        Id = Guid.NewGuid(), ActivityId = activity.Id, DefinitionId = definitionId, RecipientKind = kind,
        RecipientId = recipientId, RecipientName = name, ActivityTitle = activity.Title,
        IssuerName = activity.OrganizerCampusCode is null ? activity.ClubName : "Công tác sinh viên", IssuedAtUtc = issuedAt
    };

    private static async Task<IResult> Definitions(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await AuthorizeDefinitions(activityId, db, http, ct);
        var rows = await db.Set<ActivityCertificateDefinition>().AsNoTracking().Where(x => x.ActivityId == activityId)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Role, x.Title, hasFrame = x.FramePng != null }).ToListAsync(ct);
        return Results.Ok(new { items = rows, locked = await Locked(activity.Id, db, ct) });
    }

    private static async Task<IResult> AddDefinition(int activityId, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await AuthorizeDefinitions(activityId, db, http, ct);
        var input = await Body<DefinitionInput>(http, ct);
        var title = input.Title?.Trim();
        Require(input.Role is "PARTICIPANT" or "STAFF" or "HOST" or "HOST_CLUB" && title is { Length: > 0 and <= 200 }
            && ValidPng(input.BadgePng) && (input.FramePng is null || ValidPng(input.FramePng)),
            400, "INVALID_CERTIFICATE", "Choose a role, title and transparent PNG assets of at most 90 KiB each.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            Require(!await Locked(activityId, db, ct), 409, "CERTIFICATES_LOCKED", "Recognition was finalized.");
            if (input.Role == "HOST_CLUB")
                Require(activity.OrganizerCampusCode is not null
                    && (http.User.IsInRole(AuthRoles.StudentAffairsAdmin) || http.User.IsInRole(AuthRoles.Admin)),
                    403, "HOST_CERTIFICATE_DENIED", "Only student affairs can configure recognition for a host club.");
            Require(await db.Set<ActivityCertificateDefinition>().CountAsync(x => x.ActivityId == activityId, ct) < 30,
                409, "CERTIFICATE_LIMIT", "An activity can have at most 30 certificate definitions.");
            var row = new ActivityCertificateDefinition
            {
                Id = Guid.NewGuid(), ActivityId = activityId, Role = input.Role!, Title = title!,
                BadgePng = input.BadgePng!, FramePng = input.FramePng, CreatedAtUtc = Now(http)
            };
            db.Add(row);
            return Results.Ok(new { row.Id, row.Role, row.Title, hasFrame = row.FramePng is not null });
        }, ct);
    }

    private static async Task<IResult> RemoveDefinition(int activityId, Guid id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        await AuthorizeDefinitions(activityId, db, http, ct);
        return await ApiOperations.ExecuteAsync(http, db, new { id }, async () =>
        {
            Require(!await Locked(activityId, db, ct), 409, "CERTIFICATES_LOCKED", "Recognition was finalized.");
            var row = await db.Set<ActivityCertificateDefinition>().SingleOrDefaultAsync(x => x.ActivityId == activityId && x.Id == id, ct);
            if (row is null) return Results.NotFound();
            Require(row.Role != "HOST_CLUB" || http.User.IsInRole(AuthRoles.StudentAffairsAdmin) || http.User.IsInRole(AuthRoles.Admin),
                403, "HOST_CERTIFICATE_DENIED", "Only student affairs can change recognition for a host club.");
            db.Remove(row);
            return Results.NoContent();
        }, ct);
    }

    private static Task<IResult> Mine(int? page, ActivityDbContext db, HttpContext http, CancellationToken ct) =>
        List("STUDENT", http.User.GetUserId(), page, db, ct);

    private static async Task<IResult> GetDisplay(ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var row = await db.Set<ActivityCertificateDisplay>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        return Results.Ok(await Display(row ?? new ActivityCertificateDisplay { UserId = userId }, db, ct));
    }

    private static async Task<IResult> SaveDisplay(ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var input = await Body<DisplayInput>(http, ct);
        Require(input.BadgeX is >= 0 and <= 100 && input.BadgeY is >= 0 and <= 100 && input.BadgeSize is >= 5 and <= 40,
            400, "INVALID_BADGE_PLACEMENT", "Choose a valid badge position and size.");
        return await ApiOperations.ExecuteAsync(http, db, input, async () =>
        {
            var row = await db.Set<ActivityCertificateDisplay>().SingleOrDefaultAsync(x => x.UserId == userId, ct);
            Require((row?.Version ?? Guid.Empty) == input.ExpectedVersion, 409, "STALE_CERTIFICATE_DISPLAY", "Reload the profile decorations.");
            if (row is null)
            {
                row = new ActivityCertificateDisplay { UserId = userId };
                db.Add(row);
            }
            row.BadgeId = input.BadgeId;
            row.FrameId = input.FrameId;
            row.BadgeX = input.BadgeX;
            row.BadgeY = input.BadgeY;
            row.BadgeSize = input.BadgeSize;
            row.Version = Guid.NewGuid();
            var display = await Display(row, db, ct);
            Require((row.BadgeId is null || display.BadgePng is not null) && (row.FrameId is null || display.FramePng is not null),
                403, "CERTIFICATE_ASSET_NOT_OWNED", "Choose decorations from certificates issued to your account.");
            return Results.Ok(display);
        }, ct);
    }

    private static async Task<DisplayResponse> Display(ActivityCertificateDisplay row, ActivityDbContext db, CancellationToken ct)
    {
        var ids = new[] { row.BadgeId, row.FrameId }.OfType<Guid>().Distinct().ToArray();
        var assets = await db.Set<ActivityCertificate>().AsNoTracking()
            .Where(x => x.RecipientKind == "STUDENT" && x.RecipientId == row.UserId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Definition.BadgePng, x.Definition.FramePng }).ToListAsync(ct);
        return new(row.Version, row.BadgeId, row.FrameId, row.BadgeX, row.BadgeY, row.BadgeSize,
            assets.SingleOrDefault(x => x.Id == row.BadgeId)?.BadgePng, assets.SingleOrDefault(x => x.Id == row.FrameId)?.FramePng);
    }

    private static async Task<IResult> ForClub(int clubId, int? page, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var scope = await Authorize(clubId, http, ct);
        Require(scope.Club.IsApprovedMember || scope.Manager || scope.Reviewer, 403, "CLUB_CERTIFICATES_DENIED", "Club access is required.");
        return await List("CLUB", clubId, page, db, ct);
    }

    private static async Task<IResult> List(string kind, int id, int? page, ActivityDbContext db, CancellationToken ct)
    {
        const int size = 12;
        var current = page ?? 1;
        Require(current is >= 1 and <= 10000, 400, "INVALID_CERTIFICATE_PAGE", "Choose a valid certificate page.");
        var rows = await db.Set<ActivityCertificate>().AsNoTracking().Where(x => x.RecipientKind == kind && x.RecipientId == id)
            .OrderByDescending(x => x.ActivityId).ThenBy(x => x.Id).Skip((current - 1) * size).Take(size + 1)
            .Select(x => new
            {
                x.Id, x.ActivityId, x.ActivityTitle, x.RecipientName, x.IssuerName, x.IssuedAtUtc,
                x.Definition.Title, x.Definition.Role, x.Definition.BadgePng, x.Definition.FramePng
            }).ToListAsync(ct);
        return Results.Ok(new { items = rows.Take(size), hasMore = rows.Count > size });
    }

    private static async Task<ClubActivity> AuthorizeDefinitions(int id, ActivityDbContext db, HttpContext http, CancellationToken ct)
    {
        var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == id, ct);
        Require(activity is not null, 404, "ACTIVITY_NOT_FOUND", "Activity does not exist.");
        var scope = await CampusActivityEndpoints.OrganizerScope(activity!, db, http, ct);
        Require(scope.Manager || activity!.OrganizerCampusCode is not null && scope.Reviewer
            && scope.Account.CampusCode == activity.OrganizerCampusCode,
            403, "CERTIFICATE_CONFIGURATION_DENIED", "Only the activity organizer can configure certificates.");
        return activity!;
    }

    private static Task<bool> Locked(int id, ActivityDbContext db, CancellationToken ct) =>
        db.Set<ActivityClosingReport>().AnyAsync(x => x.ActivityId == id && (x.Status == "APPROVED" || x.Status == "FINALIZED"), ct);

    private static bool ValidPng(string? value)
    {
        if (value is null || value.Length > 122880) return false;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value); }
        catch (FormatException) { return false; }
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) || bytes[25] is not (4 or 6)) return false;
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        return width is > 0 and <= 2048 && height is > 0 and <= 2048;
    }

    private sealed record DefinitionInput(string? Role, string? Title, string? BadgePng, string? FramePng);
    private sealed record DisplayInput(Guid ExpectedVersion, Guid? BadgeId, Guid? FrameId, int BadgeX, int BadgeY, int BadgeSize);
    private sealed record DisplayResponse(Guid Version, Guid? BadgeId, Guid? FrameId, int BadgeX, int BadgeY, int BadgeSize,
        string? BadgePng, string? FramePng);
}
