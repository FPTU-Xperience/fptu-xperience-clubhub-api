using System.Security.Claims;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Extensions;
using ClubService.Mappers;
using ClubService.Models;
using Microsoft.EntityFrameworkCore;
using ClubReportHub.Shared.Experience;

namespace ClubService.Endpoints;

public static class ApplicationEndpoints
{
    public static void MapApplicationEndpoints(this WebApplication app)
    {
        var clubs = app.MapGroup("/api/clubs")
            .WithTags("Club Applications")
            .RequireAuthorization(AuthPolicies.BusinessAccess);

        // GET /api/clubs/applications - Get all applications (Admin only)
        clubs.MapGet("/applications", GetAllApplications)
            .WithName("GetAllApplications")
            .WithDescription("Get all club creation applications (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);

        // GET /api/clubs/applications/me - Get my applications
        clubs.MapGet("/applications/me", GetMyApplications)
            .WithName("GetMyApplications")
            .WithDescription("Get current user's club creation applications");

        clubs.MapGet("/applications/{applicationId:int}", GetApplication)
            .WithName("GetApplication")
            .WithDescription("Get a club creation application as its owner or reviewer");

        // POST /api/clubs/applications - Submit new application
        clubs.MapPost("/applications", SubmitApplication)
            .WithName("SubmitApplication")
            .WithDescription("Submit a new club creation application")
            .RequireAuthorization(AuthPolicies.ClubMemberOnly);

        // PUT /api/clubs/applications/{applicationId} - Update application (resubmit)
        clubs.MapPut("/applications/{applicationId:int}", UpdateApplication)
            .WithName("UpdateApplication")
            .WithDescription("Update and resubmit a club creation application")
            .RequireAuthorization(AuthPolicies.ClubMemberOnly);

        // POST /api/clubs/applications/{applicationId}/approve - Approve application (Admin only)
        clubs.MapPost("/applications/{applicationId:int}/approve", ApproveApplication)
            .WithName("ApproveApplication")
            .WithDescription("Approve a club creation application (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);

        // POST /api/clubs/applications/{applicationId}/request-revision - Request revision (Admin only)
        clubs.MapPost("/applications/{applicationId:int}/request-revision", RequestRevision)
            .WithName("RequestApplicationRevision")
            .WithDescription("Request revision for a club application (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);

        // POST /api/clubs/applications/{applicationId}/reject - Reject application (Admin only)
        clubs.MapPost("/applications/{applicationId:int}/reject", RejectApplication)
            .WithName("RejectApplication")
            .WithDescription("Reject a club creation application (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
    }

    private static bool GlobalReviewer(ClaimsPrincipal user) => user.IsInRole(AuthRoles.Admin) || user.IsInRole(AuthRoles.SystemAdmin);
    private static bool InScope(ClaimsPrincipal user, string campus) => GlobalReviewer(user) || user.GetCampus() == campus;

    private static async Task<IResult> GetAllApplications(string? status, string? campus, ClaimsPrincipal user, ClubDbContext db, CancellationToken ct)
    {
        var query = db.ClubCreationApplications.AsNoTracking();
        if (!GlobalReviewer(user)) query = query.Where(x => x.CampusCode == user.GetCampus());
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status is not (ClubApplicationStatuses.Submitted or ClubApplicationStatuses.NeedsRevision or ClubApplicationStatuses.Approved or ClubApplicationStatuses.Rejected))
                return Results.BadRequest(new { message = "Invalid application status. Use Submitted, NeedsRevision, Approved, or Rejected." });
            query = query.Where(x => x.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(campus))
        {
            if (!CampusCodes.IsValid(campus)) return Results.BadRequest(new { message = "Invalid campus." });
            var code = campus.Trim().ToUpperInvariant();
            if (!GlobalReviewer(user) && code != user.GetCampus()) return Results.Forbid();
            if (code != CampusCodes.Global) query = query.Where(x => x.CampusCode == code);
        }
        return Results.Ok((await query.OrderByDescending(x => x.SubmittedAtUtc).ToListAsync(ct)).Select(ClubMappers.ToApplicationResponse));
    }

    private static async Task<IResult> GetApplication(int applicationId, ClaimsPrincipal user, ClubDbContext db, CancellationToken ct)
    {
        var row = await db.ClubCreationApplications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == applicationId, ct);
        if (row is null) return Results.NotFound();
        if (row.RequesterUserId != user.GetUserId() && (!user.IsStudentAffairsAdministrator() || !InScope(user, row.CampusCode))) return Results.Forbid();
        return Results.Ok(ClubMappers.ToApplicationResponse(row));
    }

    private static async Task<IResult> GetMyApplications(ClaimsPrincipal user, ClubDbContext db, CancellationToken ct) =>
        Results.Ok((await db.ClubCreationApplications.AsNoTracking().Where(x => x.RequesterUserId == user.GetUserId()).OrderByDescending(x => x.SubmittedAtUtc).ToListAsync(ct)).Select(ClubMappers.ToApplicationResponse));

    private static async Task<(InternalUserProfile? Profile, IResult? Error)> ProfileAsync(int id, HttpContext http, CancellationToken ct)
    {
        var client = http.RequestServices.GetService<CurrentUserProfileClient>();
        if (client is null) return (null, ApiOperations.Error(503, "PROFILE_SERVICE_UNAVAILABLE", "Canonical account service is unconfigured."));
        try
        {
            var profile = (await client.GetAsync([id], ct)).GetValueOrDefault(id);
            if (profile is null || !profile.IsActive || profile.IsLocked || !CampusCodes.FiveCampuses.Contains(profile.CampusCode)
                || string.IsNullOrWhiteSpace(profile.DisplayName) || profile.DisplayName.Length > 200 || string.IsNullOrWhiteSpace(profile.Email))
                return (null, ApiOperations.Error(422, "ACCOUNT_NOT_ELIGIBLE", "Choose an active, unlocked campus account."));
            return (profile, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        { return (null, ApiOperations.Error(503, "PROFILE_SERVICE_UNAVAILABLE", "Canonical account service is unavailable.")); }
    }

    private static async Task<IResult> SubmitApplication(CreateClubApplicationRequest request, ClaimsPrincipal user, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!DiscoveryTags.TryNormalize(request.TagSelection, false, out var tags))
            return ApiOperations.Error(400, "INVALID_TAGS", DiscoveryTags.InvalidMessage);
        var (profile, error) = await ProfileAsync(user.GetUserId(), http, ct); if (error is not null) return error;
        if (request.CampusCode is not null && request.CampusCode != profile!.CampusCode) return Results.Forbid();
        var canonical = request with { CampusCode = profile!.CampusCode, FounderFullName = profile.DisplayName, FounderEmail = profile.Email };
        var code = ValidationExtensions.NormalizeOrGenerateClubCode(request.Code, user.GetUserId());
        var validation = ValidationExtensions.ValidateClubApplication(canonical, code);
        if (validation is not null) return Results.BadRequest(new { message = validation });
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (await db.ClubManagerAssignments.AnyAsync(x => x.ManagerUserId == user.GetUserId() && x.IsActive, ct))
                return Results.Conflict(new { message = "You already manage a club." });
            if (await db.ClubCreationApplications.AnyAsync(x => x.RequesterUserId == user.GetUserId() && x.Status == ClubApplicationStatuses.Submitted, ct))
                return Results.Conflict(new { message = "An application is already awaiting review." });
            if (await db.Clubs.AnyAsync(x => x.Code == code, ct)) return Results.Conflict(new { message = "Club code already exists." });
            var row = new ClubCreationApplication
            {
                RequesterUserId = user.GetUserId(),
                TagSelectionJson = request.TagSelection is null ? null : DiscoveryTags.Store(tags)
            };
            ClubMappers.ApplyClubApplicationRequest(row, canonical, code); db.Add(row); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/clubs/applications/{row.Id}", ClubMappers.ToApplicationResponse(row));
        }, ct);
    }

    private static async Task<IResult> UpdateApplication(int applicationId, CreateClubApplicationRequest request, ClaimsPrincipal user, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!DiscoveryTags.TryNormalize(request.TagSelection, false, out var tags))
            return ApiOperations.Error(400, "INVALID_TAGS", DiscoveryTags.InvalidMessage);
        var row = await db.ClubCreationApplications.SingleOrDefaultAsync(x => x.Id == applicationId, ct);
        if (row is null) return Results.NotFound();
        if (row.RequesterUserId != user.GetUserId()) return Results.Forbid();
        var (profile, error) = await ProfileAsync(user.GetUserId(), http, ct); if (error is not null) return error;
        if (profile!.CampusCode != row.CampusCode || request.CampusCode is not null && request.CampusCode != row.CampusCode) return Results.Forbid();
        var canonical = request with
        {
            CampusCode = row.CampusCode,
            FounderFullName = profile.DisplayName,
            FounderEmail = profile.Email,
            LogoUrl = request.LogoUrl ?? row.LogoUrl,
            ExpectedLocation = request.ExpectedLocation ?? row.ExpectedLocation,
            ExpectedSchedule = request.ExpectedSchedule ?? row.ExpectedSchedule,
            MajorEvents = request.MajorEvents ?? row.MajorEvents,
            EquipmentNeeds = request.EquipmentNeeds ?? row.EquipmentNeeds,
            AdvisorNeeded = request.AdvisorNeeded || row.AdvisorNeeded
        };
        var code = string.IsNullOrWhiteSpace(request.Code) ? row.Code : ValidationExtensions.NormalizeOrGenerateClubCode(request.Code, row.RequesterUserId);
        var validation = ValidationExtensions.ValidateClubApplication(canonical, code);
        if (validation is not null) return Results.BadRequest(new { message = validation });
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            if (request.ExpectedVersion != row.Version || row.Status != ClubApplicationStatuses.NeedsRevision) return Results.Conflict(new { message = "Reload an application requiring revision." });
            if (await db.Clubs.AnyAsync(x => x.Code == code, ct)) return Results.Conflict(new { message = "Club code already exists." });
            if (request.TagSelection is not null) row.TagSelectionJson = DiscoveryTags.Store(tags);
            ClubMappers.ApplyClubApplicationRequest(row, canonical, code); row.Version = Guid.NewGuid(); row.Status = ClubApplicationStatuses.Submitted;
            row.ReviewNote = null; row.ReviewConditions = null; row.ReviewerSignature = null; row.ReviewedAtUtc = null; row.ReviewedByUserId = null; row.SubmittedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct); return Results.Ok(ClubMappers.ToApplicationResponse(row));
        }, ct);
    }

    private static async Task<IResult> ApproveApplication(int applicationId, ReviewClubApplicationRequest request, ClaimsPrincipal user, ClubDbContext db, HttpContext http, CancellationToken ct)
    {
        var row = await db.ClubCreationApplications.SingleOrDefaultAsync(x => x.Id == applicationId, ct);
        if (row is null) return Results.NotFound();
        if (!InScope(user, row.CampusCode)) return Results.Forbid();
        var validation = ValidationExtensions.ValidateReviewContent(request.Note, request.Conditions, request.ReviewerSignature);
        if (validation is not null) return Results.BadRequest(new { message = validation });
        var managerId = request.ManagerUserId ?? row.RequesterUserId;
        var (manager, error) = await ProfileAsync(managerId, http, ct); if (error is not null) return error;
        if (manager!.CampusCode != row.CampusCode) return ApiOperations.Error(422, "CAMPUS_MISMATCH", "The selected manager must belong to the club campus.");
        var (requester, requesterError) = managerId == row.RequesterUserId ? (manager, (IResult?)null) : await ProfileAsync(row.RequesterUserId, http, ct);
        if (requesterError is not null) return requesterError;
        if (requester!.CampusCode != row.CampusCode) return ApiOperations.Error(422, "CAMPUS_MISMATCH", "The requester campus changed; request revision before approval.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            if (request.ExpectedVersion != row.Version || row.Status != ClubApplicationStatuses.Submitted) return Results.Conflict(new { message = "Reload the pending application before reviewing." });
            if (await db.ClubManagerAssignments.AnyAsync(x => x.ManagerUserId == managerId && x.IsActive, ct)) return Results.Conflict(new { message = "Selected manager already manages a club." });
            if (await db.Clubs.AnyAsync(x => x.Code == row.Code, ct)) return Results.Conflict(new { message = "Club code already exists." });
            var club = new Club
            {
                Code = row.Code,
                Name = row.Name,
                Category = row.Category,
                CampusCode = row.CampusCode,
                TagSelectionJson = row.TagSelectionJson,
                Description = row.Description,
                LogoUrl = row.LogoUrl,
                ContactEmail = row.ContactEmail,
                ContactPhone = row.ContactPhone,
                ExpectedLocation = row.ExpectedLocation,
                MajorEvents = row.MajorEvents,
                EquipmentNeeds = row.EquipmentNeeds,
                AdvisorNeeded = row.AdvisorNeeded
            };
            club.ManagerAssignments.Add(new ClubManagerAssignment { ManagerUserId = managerId, ManagerName = manager.DisplayName });
            foreach (var account in new[] { manager, requester }.DistinctBy(x => x.Id))
                club.Memberships.Add(new ClubMembership
                {
                    UserId = account.Id,
                    FullName = account.DisplayName,
                    Email = account.Email,
                    PhoneNumber = account.Id == row.RequesterUserId ? row.ContactPhone : account.PhoneNumber ?? string.Empty,
                    Address = account.Id == row.RequesterUserId ? row.FounderOrganization : string.Empty,
                    Role = ClubMemberRoles.Member,
                    Status = ClubMembershipStatuses.Approved,
                    AcceptedClubRules = true,
                    CommittedToParticipate = true,
                    ReviewedAtUtc = DateTimeOffset.UtcNow,
                    ReviewedByUserId = user.GetUserId()
                });
            db.Clubs.Add(club); await db.SaveChangesAsync(ct);
            Stamp(row, request, user, ClubApplicationStatuses.Approved); row.CreatedClubId = club.Id;
            row.ApprovedManagerUserId = managerId; row.ApprovedManagerName = manager.DisplayName;
            db.AddOutboxMessage(new ClubCreatedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, club.Id, club.Code, club.Name), EventRoutingKeys.ClubCreated);
            await db.SaveChangesAsync(ct); return Results.Ok(ClubMappers.ToApplicationResponse(row));
        }, ct);
    }

    private static Task<IResult> RequestRevision(int applicationId, ReviewClubApplicationRequest request, ClaimsPrincipal user, ClubDbContext db, HttpContext http, CancellationToken ct) =>
        ReviewAsync(applicationId, request, user, db, http, ClubApplicationStatuses.NeedsRevision, ct);
    private static Task<IResult> RejectApplication(int applicationId, ReviewClubApplicationRequest request, ClaimsPrincipal user, ClubDbContext db, HttpContext http, CancellationToken ct) =>
        ReviewAsync(applicationId, request, user, db, http, ClubApplicationStatuses.Rejected, ct);
    private static async Task<IResult> ReviewAsync(int id, ReviewClubApplicationRequest request, ClaimsPrincipal user, ClubDbContext db, HttpContext http, string status, CancellationToken ct)
    {
        var row = await db.ClubCreationApplications.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return Results.NotFound(); if (!InScope(user, row.CampusCode)) return Results.Forbid();
        var validation = ValidationExtensions.ValidateReviewContent(request.Note, request.Conditions, request.ReviewerSignature);
        if (string.IsNullOrWhiteSpace(request.Note) || validation is not null) return Results.BadRequest(new { message = validation ?? "Provide a reason." });
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            await db.Entry(row).ReloadAsync(ct);
            if (request.ExpectedVersion != row.Version || row.Status != ClubApplicationStatuses.Submitted) return Results.Conflict(new { message = "Reload the pending application before reviewing." });
            Stamp(row, request, user, status); await db.SaveChangesAsync(ct); return Results.Ok(ClubMappers.ToApplicationResponse(row));
        }, ct);
    }
    private static void Stamp(ClubCreationApplication row, ReviewClubApplicationRequest request, ClaimsPrincipal user, string status)
    {
        row.Status = status; row.Version = Guid.NewGuid(); row.ReviewNote = request.Note?.Trim(); row.ReviewConditions = request.Conditions?.Trim();
        row.ReviewerSignature = request.ReviewerSignature?.Trim(); row.ReviewedAtUtc = DateTimeOffset.UtcNow; row.ReviewedByUserId = user.GetUserId();
    }
}
