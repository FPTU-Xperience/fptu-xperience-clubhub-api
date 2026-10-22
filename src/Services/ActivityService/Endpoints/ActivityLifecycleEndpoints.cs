using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Events;
using Microsoft.EntityFrameworkCore;
using static ActivityService.Endpoints.ActivityEndpointHelpers;

namespace ActivityService.Endpoints;

internal static class ActivityLifecycleEndpoints
{
    internal static async Task<IResult> UpdateAsync(int id, UpdateActivityDetailsRequest request,
        ActivityDbContext db, HttpContext http, ClubAccessClient access, ExperienceAuthorityClient authority,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (activity is null) return Results.NotFound();
        if (!await CanManageClubOrReviewAllAsync(activity.ClubId, http.User, access, http, cancellationToken, true)) return Results.Forbid();
        if (await db.Set<ActivityExecution>().AnyAsync(x => x.ActivityId == id, cancellationToken))
            return ApiOperations.Error(409, "ACTION_WORKFLOW_PROTECTED", "Use the application/publication workflow. Action scoring is pending policy approval.");
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            if (request.ExpectedVersion != activity.Version) return ApiOperations.Error(409, "STALE_VERSION", "Reload the activity.");
            var operation = request.Operation?.ToUpperInvariant() ?? "UPDATE";
            if (operation == "CLOSE")
            {
                if (activity.PublicationStatus != "PUBLISHED") return ApiOperations.Error(409, "INVALID_ACTIVITY_STATE", "Only published registration can be closed.");
                activity.PublicationStatus = "CLOSED";
            }
            else if (operation is "UPDATE" or "PUBLISH")
            {
                if (activity.PublicationStatus is not ("DRAFT" or "LEGACY") || activity.SourceReportId.HasValue
                    || await db.Sessions.AnyAsync(x => x.ActivityId == id, cancellationToken)
                    || await db.ActivityAttendances.AnyAsync(x => x.ActivityId == id, cancellationToken))
                    return ApiOperations.Error(409, "ACTIVITY_HISTORY_PROTECTED", "Only a standalone draft without session history can be edited or published.");
                if (request.Title is not null && request.Name is not null && request.Title.Trim() != request.Name.Trim()
                    || request.StartTimeUtc.HasValue && request.StartAt.HasValue && request.StartTimeUtc != request.StartAt
                    || request.EndTimeUtc.HasValue && request.EndAt.HasValue && request.EndTimeUtc != request.EndAt)
                    return ApiOperations.Error(400, "CONFLICTING_ALIASES", "Aliases must agree.");
                activity.Title = (request.Title ?? request.Name)?.Trim() ?? activity.Title;
                activity.Description = request.Description?.Trim() ?? activity.Description;
                activity.Location = request.Location?.Trim() ?? activity.Location;
                activity.StartTimeUtc = request.StartTimeUtc ?? request.StartAt ?? activity.StartTimeUtc;
                activity.EndTimeUtc = request.EndTimeUtc ?? request.EndAt ?? activity.EndTimeUtc;
                activity.SemesterId = request.SemesterId ?? activity.SemesterId;
                activity.EffortTier = request.EffortTier ?? activity.EffortTier;
                activity.Scale = request.Scale ?? activity.Scale;
                activity.Capacity = request.Capacity ?? activity.Capacity;
                activity.RegistrationOpensAt = request.RegistrationOpensAt ?? activity.RegistrationOpensAt;
                activity.RegistrationClosesAt = request.RegistrationClosesAt ?? activity.RegistrationClosesAt;
                activity.Eligibility = request.Eligibility ?? activity.Eligibility;
                activity.DiscoveryVisibility = request.DiscoveryVisibility ?? activity.DiscoveryVisibility;
                activity.EvidenceRequirements = request.EvidenceRequirements?.Trim() ?? activity.EvidenceRequirements;
                activity.PolicyVersion = request.PolicyVersion ?? activity.PolicyVersion;
                if (request.PillarWeights is not null)
                    activity.PillarWeightsJson = JsonSerializer.Serialize(request.PillarWeights, ActivitySessionEndpoints.JsonOptions);
                if (request.MeetingDays is not null)
                {
                    if (request.MeetingDays.Any(x => x is < 1 or > 7)) return ApiOperations.Error(400, "INVALID_WEEKDAYS", "Weekdays must be 1–7.");
                    activity.MeetingDaysCsv = string.Join(',', NormalizeMeetingDays(request.MeetingDays));
                }
                if (activity.Title.Length is 0 or > 200 || activity.Description.Length is 0 or > 2000
                    || activity.Location.Length is 0 or > 200 || activity.EndTimeUtc <= activity.StartTimeUtc
                    || activity.Capacity is <= 0 || activity.Capacity > configuration.GetValue("Activities:MaxCapacity", 100000)
                    || activity.EvidenceRequirements?.Length > 2000 || activity.Eligibility is not ("CLUB_MEMBER" or "CAMPUS_PUBLIC")
                    || activity.DiscoveryVisibility is not ("CLUB_ONLY" or "CAMPUS_PUBLIC"))
                    return ApiOperations.Error(400, "INVALID_ACTIVITY", "Check activity content, times, capacity and eligibility.");
                if (operation == "PUBLISH")
                {
                    if (string.IsNullOrWhiteSpace(activity.SemesterId) || !activity.PolicyVersion.HasValue
                        || !activity.RegistrationOpensAt.HasValue || !activity.RegistrationClosesAt.HasValue
                        || activity.RegistrationOpensAt >= activity.RegistrationClosesAt || activity.RegistrationClosesAt > activity.EndTimeUtc)
                        return ApiOperations.Error(400, "PUBLICATION_REQUIREMENTS", "Provide a semester, policy version and valid registration window.");
                    var attributes = new ContributionAttributes(activity.EffortTier, ContributionRoles.Participant, activity.Scale,
                        BonusResults.None, JsonSerializer.Deserialize<List<PillarWeight>>(activity.PillarWeightsJson, ActivitySessionEndpoints.JsonOptions) ?? []);
                    var error = VerifiedExperienceRules.Validate(attributes);
                    if (error is not null) return ApiOperations.Error(400, "INVALID_SCORING_ATTRIBUTES", error);
                    ExperiencePolicyReference? policy;
                    try { policy = await authority.ResolvePolicyAsync(activity.SemesterId, activity.PolicyVersion.Value, http, cancellationToken); }
                    catch (HttpRequestException) { return ApiOperations.Error(503, "EXPERIENCE_SERVICE_UNAVAILABLE", "Scoring policy service is unavailable."); }
                    if (policy is null) return ApiOperations.Error(404, "SCORE_POLICY_NOT_FOUND", "The policy does not belong to this semester.");
                    if (policy.ApprovalStatus != "APPROVED" || policy.Environment != configuration.GetValue("Experience:Environment", "PROJECT"))
                        return ApiOperations.Error(503, "SCORE_POLICY_UNAPPROVED", "Use an approved policy for this environment.");
                    if (!policy.StartDate.HasValue || !policy.EndDate.HasValue || activity.StartTimeUtc < policy.StartDate
                        || activity.EndTimeUtc >= policy.EndDate.Value.AddDays(1))
                        return ApiOperations.Error(400, "ACTIVITY_OUTSIDE_SEMESTER", "The activity must fit the authoritative semester period.");
                    activity.SemesterId = policy.SemesterId;
                    activity.Version = Guid.NewGuid();
                    activity.PublicationStatus = "PUBLISHED";
                    var blueprint = new ExperienceSourceSnapshot(activity.Id, activity.ClubId, 0, policy.SemesterId, activity.Title,
                        activity.StartTimeUtc, activity.EndTimeUtc, activity.Version, Guid.Empty, "SCHEDULED", false, attributes, policy);
                    activity.ScoringSnapshotJson = JsonSerializer.Serialize(blueprint, ActivitySessionEndpoints.JsonOptions);
                    var recipients = (await access.GetMyAccessAsync(http.GetBearerToken(), cancellationToken, true)).FirstOrDefault(x => x.ClubId == activity.ClubId)?.MemberUserIds ?? [];
                    db.AddOutboxMessage(new ExperienceNotificationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, activity.ClubId,
                        recipients.ToArray(), "ACTIVITY_PUBLISHED", "Hoạt động mới của CLB", $"/activities/{activity.Id}"), ExperienceRoutingKeys.Notification);
                }
                else activity.PublicationStatus = "DRAFT";
            }
            else return ApiOperations.Error(400, "INVALID_OPERATION", "Use UPDATE, PUBLISH or CLOSE.");
            if (operation != "PUBLISH") activity.Version = Guid.NewGuid();
            activity.UpdatedAtUtc = ActivitySessionEndpoints.Now(http);
            db.SessionAudits.Add(new ActivitySessionAudit
            {
                ActivityId = id,
                Action = operation,
                ActorId = http.User.GetUserId(),
                Version = activity.Version,
                CreatedAtUtc = activity.UpdatedAtUtc,
                SnapshotJson = JsonSerializer.Serialize(ToResponse(activity), ActivitySessionEndpoints.JsonOptions)
            });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(activity));
        }, cancellationToken);
    }
}
