using System.Security.Claims;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Infrastructure;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static ActivityService.Endpoints.ActivityEndpointHelpers;

namespace ActivityService.Endpoints;

public static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivityEndpoints(
        this IEndpointRouteBuilder app)
    {
        var activities = app
            .MapGroup("/api/activities")
            .WithTags("Activities")
            .AddEndpointFilter<ActivityTimelineReadFilter>()
            .AddEndpointFilter<ActivityResourceReadFilter>()
            .AddEndpointFilter<ActivityPublicationReadFilter>()
            .RequireAuthorization(AuthPolicies.BusinessAccess);

        // Implemented endpoints
        MapGetActivities(activities);
        MapGetActivityById(activities);
        MapCreateActivity(activities);
        MapCreateFromApprovedReport(activities);
        activities.MapActivityWriteEndpoints();
        app.MapActivitySessionEndpoints();
        app.MapActivityVisibilityEndpoints();
        app.MapActivityExperienceSourceEndpoints();
        app.MapActivityExperienceMetricsEndpoints();
        app.MapActivityCoverImageEndpoints();
        app.MapActivityResourceEndpoints();

        return app;
    }

    private static void MapGetActivities(
        RouteGroupBuilder activities)
    {
        activities.MapGet("/", async (
            int? clubId,
            string? status,
            DateTimeOffset? from,
            DateTimeOffset? to,
            ActivityDbContext db,
            ClaimsPrincipal user,
            HttpContext httpContext,
            ClubAccessClient clubAccess,
            CancellationToken cancellationToken) =>
        {
            var query = db.Activities
                .AsNoTracking()
                .AsQueryable();

            var managedClubIds = new HashSet<int>();
            if (!CanReviewAllActivities(user))
            {
                var access = await clubAccess.GetMyAccessAsync(
                    httpContext.GetBearerToken(),
                    cancellationToken, true);

                var visibleClubIds = access
                    .Where(x => x.CanView)
                    .Select(x => x.ClubId)
                    .ToHashSet();

                query = query.Where(
                    x => visibleClubIds.Contains(x.ClubId));
                var managed = access.Where(x => x.CanManage).Select(x => x.ClubId).ToArray();
                managedClubIds = managed.ToHashSet();
                query = query.Where(x => x.PublicationStatus != "DRAFT" || managed.Contains(x.ClubId));
            }

            if (clubId.HasValue)
            {
                query = query.Where(
                    x => x.ClubId == clubId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(
                    x => x.Status == status);
            }

            if (from.HasValue)
            {
                query = query.Where(
                    x => x.StartTimeUtc >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(
                    x => x.StartTimeUtc <= to.Value);
            }

            var rows = await query
                .OrderBy(x => x.StartTimeUtc)
                .Include(x => x.Participants)
                .Include(x => x.Attendances)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);

            return Results.Ok(
                rows.Select(row => ToResponse(row, CanReviewAllActivities(user) || managedClubIds.Contains(row.ClubId) ? null : user.GetUserId())));
        });
    }

    private static void MapGetActivityById(
        RouteGroupBuilder activities)
    {
        activities.MapGet("/{id:int}", async (
            int id,
            ActivityDbContext db,
            ClaimsPrincipal user,
            HttpContext httpContext,
            ClubAccessClient clubAccess,
            CancellationToken cancellationToken) =>
        {
            var activity = await db.Activities
                .AsNoTracking()
                .Include(x => x.Participants)
                .Include(x => x.Attendances)
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (activity is null)
            {
                return Results.NotFound();
            }

            var canManage = CanReviewAllActivities(user);
            if (!canManage)
            {
                var access = await clubAccess.GetMyAccessAsync(
                    httpContext.GetBearerToken(),
                    cancellationToken, true);

                var canView = access.Any(x => x.ClubId == activity.ClubId && x.CanView);
                canManage = access.Any(x => x.ClubId == activity.ClubId && x.CanManage);
                if (activity.PublicationStatus == "DRAFT" && !access.Any(x => x.ClubId == activity.ClubId && x.CanManage)) return Results.Forbid();
                if (!canView)
                {
                    return Results.Forbid();
                }
            }

            return Results.Ok(ToResponse(activity, canManage ? null : user.GetUserId()));
        });
    }

    private static void MapCreateActivity(RouteGroupBuilder activities)
    {
        activities.MapPost("/", async (
            CreateActivityRequest request,
            ActivityDbContext db,
            ClaimsPrincipal user,
            HttpContext httpContext,
            ClubAccessClient clubAccess,
            CancellationToken cancellationToken) =>
        {
            if (request.Title is not null && request.Name is not null
                && !string.Equals(request.Title.Trim(), request.Name.Trim(), StringComparison.Ordinal))
                return Results.BadRequest(new { message = "Title and name must agree when both are provided." });
            if (request.StartTimeUtc.HasValue && request.StartAt.HasValue && request.StartTimeUtc != request.StartAt)
                return Results.BadRequest(new { message = "StartTimeUtc and startAt must agree when both are provided." });
            if (request.EndTimeUtc.HasValue && request.EndAt.HasValue && request.EndTimeUtc != request.EndAt)
                return Results.BadRequest(new { message = "EndTimeUtc and endAt must agree when both are provided." });

            var title = request.Title ?? request.Name;
            var start = request.StartTimeUtc ?? request.StartAt;
            var end = request.EndTimeUtc ?? request.EndAt;
            var validationError = ValidateCreateRequest(
                request.ClubId,
                title,
                request.Description,
                request.Location,
                start,
                end);
            if (validationError is not null)
            {
                return Results.BadRequest(new { message = validationError });
            }

            if (string.IsNullOrWhiteSpace(request.ClubName) || request.ClubName.Length > 200
                || title!.Length > 200 || request.Description.Length > 2000
                || request.Location.Length > 200
                || (request.MeetingDays is not null && request.MeetingDays.Any(day => day is < 1 or > 7)))
            {
                return Results.BadRequest(new { message = "Club name, content or meeting days are invalid." });
            }

            if (!await CanManageClubOrReviewAllAsync(
                    request.ClubId,
                    user,
                    clubAccess,
                    httpContext,
                    cancellationToken))
            {
                return Results.Forbid();
            }

            var managedClub = (await clubAccess.GetMyAccessAsync(httpContext.GetBearerToken(), cancellationToken, true))
                .FirstOrDefault(x => x.ClubId == request.ClubId && x.CanManage);
            var canonicalClubName = managedClub?.ClubName;
            if (canonicalClubName is null)
            {
                var authority = httpContext.RequestServices.GetService<ClubReportHub.Shared.Experience.ClubExperienceAuthorityClient>();
                if (authority is null) return ApiOperations.Error(503, "CLUB_AUTHORITY_UNAVAILABLE", "Current club authority is unavailable.");
                ClubReportHub.Shared.Experience.ClubExperienceAuthority? club;
                try { club = await authority.GetAsync(request.ClubId, null, cancellationToken); }
                catch (HttpRequestException) { return ApiOperations.Error(503, "CLUB_AUTHORITY_UNAVAILABLE", "Current club authority is unavailable."); }
                if (club is null) return Results.NotFound();
                if (!club.IsActive) return ApiOperations.Error(409, "CLUB_INACTIVE", "The club is inactive.");
                canonicalClubName = club.Name;
            }
            var activity = new ClubActivity
            {
                ClubId = request.ClubId,
                ClubName = canonicalClubName,
                Title = title.Trim(),
                Description = request.Description.Trim(),
                StartTimeUtc = start!.Value,
                EndTimeUtc = end!.Value,
                MeetingDaysCsv = string.Join(',', NormalizeMeetingDays(request.MeetingDays)),
                Location = request.Location.Trim(),
                Status = ActivityStatuses.Scheduled,
                PublicationStatus = "DRAFT",
                IsInternal = request.IsInternal ?? request.DiscoveryVisibility != "CAMPUS_PUBLIC",
                DiscoveryVisibility = (request.IsInternal ?? request.DiscoveryVisibility != "CAMPUS_PUBLIC") ? "CLUB_ONLY" : "CAMPUS_PUBLIC",
                CreatedByUserId = user.GetUserId(),
                SemesterId = request.SemesterId,
                EffortTier = request.EffortTier ?? "T1",
                Scale = request.Scale ?? "Club",
                PillarWeightsJson = System.Text.Json.JsonSerializer.Serialize(request.PillarWeights ?? [], ActivitySessionEndpoints.JsonOptions),
                PolicyVersion = request.PolicyVersion,
                Capacity = request.Capacity,
                RegistrationOpensAt = request.RegistrationOpensAt,
                RegistrationClosesAt = request.RegistrationClosesAt,
                Eligibility = request.Eligibility ?? "CLUB_MEMBER",
                EvidenceRequirements = request.EvidenceRequirements
            };

            return await ApiOperations.ExecuteAsync(httpContext, db, request, async () =>
            {
                if (activity.Capacity is <= 0 || activity.Eligibility is not ("CLUB_MEMBER" or "CAMPUS_PUBLIC")
                    || activity.DiscoveryVisibility is not ("CLUB_ONLY" or "CAMPUS_PUBLIC")) return Results.BadRequest();
                db.Activities.Add(activity); await db.SaveChangesAsync(cancellationToken);
                db.AddOutboxMessage(new ActivityCreatedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, activity.Id,
                    activity.ClubId, activity.ClubName, activity.Title, activity.StartTimeUtc, [user.GetUserId()]), EventRoutingKeys.ActivityCreated);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created($"/api/activities/{activity.Id}", ToResponse(activity));
            }, cancellationToken, required: false);
        })
        .WithName("CreateActivity")
        .WithDescription("Create an activity for a managed club.");
    }

    private static void MapCreateFromApprovedReport(RouteGroupBuilder activities)
    {
        activities.MapPost("/from-approved-report", async (
            CreateActivityFromApprovedReportRequest request,
            ActivityDbContext db,
            ClaimsPrincipal user,
            ReportVerificationClient reportClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (request.ReportId <= 0 || request.ReportDetailId <= 0 || request.ClubId <= 0)
            {
                return Results.BadRequest(new { message = "Report, report detail, and club identifiers must be positive." });
            }

            if (string.IsNullOrWhiteSpace(request.Title)
                || string.IsNullOrWhiteSpace(request.Description)
                || string.IsNullOrWhiteSpace(request.Location))
            {
                return Results.BadRequest(new { message = "Title, description, and location are required." });
            }

            var report = await reportClient.GetReportAsync(request.ReportId, httpContext.GetBearerToken(), cancellationToken);
            if (report is null)
            {
                return Results.BadRequest(new { message = "Report not found." });
            }

            if (report.ClubId != request.ClubId)
            {
                return Results.BadRequest(new { message = "The report belongs to another club." });
            }

            if (!string.Equals(report.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = "Only approved reports can be published as activities." });
            }

            if (!report.Details.Any(d => d.Id == request.ReportDetailId))
            {
                return Results.BadRequest(new { message = "The report detail does not belong to the specified report." });
            }

            var existing = await db.Activities
                .Include(x => x.Participants)
                .Include(x => x.Attendances)
                .AsSplitQuery()
                .SingleOrDefaultAsync(x => x.SourceReportId == request.ReportId, cancellationToken);
            if (existing is not null)
            {
                return Results.Ok(ToResponse(existing));
            }

            var localStart = new DateTimeOffset(
                request.ActivityDate.ToDateTime(new TimeOnly(9, 0)),
                TimeSpan.FromHours(7));
            var activity = new ClubActivity
            {
                SourceReportId = request.ReportId,
                SourceReportDetailId = request.ReportDetailId,
                ClubId = request.ClubId,
                ClubName = request.ClubName.Trim(),
                Title = request.Title.Trim(),
                Description = request.Description.Trim(),
                StartTimeUtc = localStart.ToUniversalTime(),
                EndTimeUtc = localStart.AddHours(3).ToUniversalTime(),
                Location = request.Location.Trim(),
                Status = ActivityStatuses.Scheduled,
                CreatedByUserId = user.GetUserId()
            };

            IDbContextTransaction? transaction = null;
            if (db.Database.IsRelational())
            {
                transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            }

            db.Activities.Add(activity);
            await db.SaveChangesAsync(cancellationToken);

            db.AddOutboxMessage(
                new ActivityCreatedEvent(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    activity.Id,
                    activity.ClubId,
                    activity.ClubName,
                    activity.Title,
                    activity.StartTimeUtc),
                EventRoutingKeys.ActivityCreated);

            await db.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return Results.Created($"/api/activities/{activity.Id}", ToResponse(activity));
        })
        .WithName("CreateActivityFromApprovedReport")
        .WithDescription("Idempotently publish an approved future-event report as an activity.")
        .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
    }

    private static string? ValidateCreateRequest(
        int clubId,
        string? title,
        string? description,
        string? location,
        DateTimeOffset? startTimeUtc,
        DateTimeOffset? endTimeUtc)
    {
        if (clubId <= 0) return "ClubId must be positive.";
        if (string.IsNullOrWhiteSpace(title)) return "Title is required.";
        if (string.IsNullOrWhiteSpace(description)) return "Description is required.";
        if (string.IsNullOrWhiteSpace(location)) return "Location is required.";
        if (!startTimeUtc.HasValue || !endTimeUtc.HasValue) return "Start and end times are required.";
        if (endTimeUtc <= startTimeUtc) return "End time must be later than start time.";
        return null;
    }
}
