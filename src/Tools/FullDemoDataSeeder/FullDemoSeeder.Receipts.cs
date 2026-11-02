using System.Text.Json;
using ActivityService.Models;
using AdminService.Data;
using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace FullDemoDataSeeder;

public sealed partial class FullDemoSeeder
{
    // These observations supplement API command receipts. They are evidence for
    // operator reconciliation, never authority to blindly delete ledger history.
    private async Task ObserveAsync<T>(string service, DbContext db, IQueryable<T> query) where T : class
    {
        foreach (var row in await query.AsNoTracking().ToArrayAsync()) journal.Observe(service, db.Entry(row), true);
    }

    private async Task CaptureReceiptsAsync()
    {
        var userIds = OwnedUserIds().ToArray(); var clubIds = anchors.Select(c => c.Id).ToArray();
        var recordedActivityIds = journal.Rows.Where(r => r.Service == "Activity" && r.Table == "Activities")
            .Select(r => JsonSerializer.Deserialize<JsonElement>(r.KeyJson).GetProperty("Id").GetInt32()).ToArray();
        var activityIds = await activities.Activities.Where(a => clubIds.Contains(a.ClubId)
            && (recordedActivityIds.Contains(a.Id) || a.Title.StartsWith("[Demo]"))).Select(a => a.Id).ToArrayAsync();
        var reportIds = await reports.Reports.Where(r => r.SeedKey != null && r.SeedKey.StartsWith("FULLDEMO_V2:")).Select(r => r.Id).ToArrayAsync();
        var contributions = admin.Contributions.Where(c => userIds.Contains(c.UserId) || c.UserId == protectedUser.Id && c.ActivityId != null && activityIds.Contains(c.ActivityId.Value));
        var contributionIds = await contributions.Select(c => c.Id).ToArrayAsync();
        await ObserveAsync("Auth", auth, auth.Users.Where(u => userIds.Contains(u.Id)));
        await ObserveAsync("Auth", auth, auth.Set<CanonicalUserProfile>().Where(p => userIds.Contains(p.UserId)));
        await ObserveAsync("Auth", auth, auth.RefreshTokens.Where(t => userIds.Contains(t.UserId)));
        await ObserveAsync("Club", clubs, clubs.ClubManagerAssignments.Where(m => userIds.Contains(m.ManagerUserId)));
        await ObserveAsync("Club", clubs, clubs.ClubMemberships.Where(m => clubIds.Contains(m.ClubId) && (userIds.Contains(m.UserId) || m.UserId == protectedUser.Id)));
        var memberIds = await clubs.ClubMemberships.Where(m => clubIds.Contains(m.ClubId) && userIds.Contains(m.UserId)).Select(m => m.Id).ToArrayAsync();
        await ObserveAsync("Club", clubs, clubs.MembershipActions.Where(m => memberIds.Contains(m.MembershipId)));
        await ObserveAsync("Activity", activities, activities.Activities.Where(a => activityIds.Contains(a.Id)));
        await ObserveAsync("Activity", activities, activities.Set<ActivityApplication>().Where(a => userIds.Contains(a.CreatedBy)));
        var applicationIds = await activities.Set<ActivityApplication>().Where(a => userIds.Contains(a.CreatedBy)).Select(a => a.Id).ToArrayAsync();
        await ObserveAsync("Activity", activities, activities.Set<ActivityApplicationHistory>().Where(a => applicationIds.Contains(a.ApplicationId)));
        await ObserveAsync("Activity", activities, activities.Set<ActivityExecution>().Where(a => activityIds.Contains(a.ActivityId)));
        await ObserveAsync("Activity", activities, activities.Set<ActivityActionRule>().Where(a => activityIds.Contains(a.ActivityId)));
        await ObserveAsync("Activity", activities, activities.Set<ActivityRecruitment>().Where(a => activityIds.Contains(a.ActivityId)));
        var recruitmentIds = await activities.Set<ActivityRecruitment>().Where(a => activityIds.Contains(a.ActivityId)).Select(a => a.Id).ToArrayAsync();
        await ObserveAsync("Activity", activities, activities.Set<ActivityRecruitmentPosition>().Where(a => recruitmentIds.Contains(a.RecruitmentId)));
        await ObserveAsync("Activity", activities, activities.Set<ActivityStaffAssignment>().Where(a => activityIds.Contains(a.ActivityId)));
        await ObserveAsync("Activity", activities, activities.Set<ActivityExecutionAudit>().Where(a => activityIds.Contains(a.ActivityId)));
        await ObserveAsync("Activity", activities, activities.Sessions.Where(a => activityIds.Contains(a.ActivityId)));
        await ObserveAsync("Activity", activities, activities.ActivityAttendances.Where(a => activityIds.Contains(a.ActivityId)));
        await ObserveAsync("Admin", admin, contributions);
        await ObserveAsync("Admin", admin, admin.SelfDeclarations.Where(c => contributionIds.Contains(c.Id)));
        await ObserveAsync("Admin", admin, admin.ContributionRevisions.Where(c => contributionIds.Contains(c.ContributionId)));
        await ObserveAsync("Admin", admin, admin.ContributionDecisions.Where(c => contributionIds.Contains(c.ContributionId)));
        await ObserveAsync("Admin", admin, admin.ExperienceLedgerEffects.Where(c => contributionIds.Contains(c.ContributionId)));
        var effectIds = await admin.ExperienceLedgerEffects.Where(c => contributionIds.Contains(c.ContributionId)).Select(c => c.Id).ToArrayAsync();
        await ObserveAsync("Admin", admin, admin.ExperienceLedgerAllocations.Where(c => effectIds.Contains(c.EffectId)));
        await ObserveAsync("Admin", admin, admin.ContributionEvidence.Where(e => userIds.Contains(e.OwnerUserId)));
        await ObserveAsync("Admin", admin, admin.VerificationAssignments.Where(a => userIds.Contains(a.AssignedBy)));
        await ObserveAsync("Admin", admin, admin.ApprovedExperiencePolicies.Where(p => p.ApprovalBasis.StartsWith("[FullDemo v2]")));
        foreach (var config in await admin.SemesterBenchmarkConfigs.AsNoTracking().ToArrayAsync())
            if (config.SemesterCode == semester.SemesterCode || journal.Rows.Any(r => r.Service == "Admin" && r.Table == "SemesterBenchmarkConfigs" && r.KeyJson.Contains(config.Id.ToString())))
                journal.Observe("Admin", admin.Entry(config), true);
        await ObserveAsync("Admin", admin, admin.Set<AcademicCalendarConfiguration>().Where(c => userIds.Contains(c.UpdatedByUserId)));
        var calendarIds = await admin.Set<AcademicCalendarConfiguration>().Where(c => userIds.Contains(c.UpdatedByUserId)).Select(c => c.Id).ToArrayAsync();
        await ObserveAsync("Admin", admin, admin.Set<AcademicCalendarRevision>().Where(c => calendarIds.Contains(c.CalendarId)));
        await ObserveAsync("Admin", admin, admin.Set<SemesterRosterImport>().Where(r => userIds.Contains(r.ActorId)));
        await ObserveAsync("Admin", admin, admin.Set<SemesterRosterRevision>().Where(r => userIds.Contains(r.ActorId)));
        var rosterRevisions = await admin.Set<SemesterRosterRevision>().Where(r => userIds.Contains(r.ActorId)).Select(r => r.Id).ToArrayAsync();
        await ObserveAsync("Admin", admin, admin.Set<SemesterRosterRow>().Where(r => rosterRevisions.Contains(r.RevisionId)));
        await ObserveAsync("Admin", admin, admin.Set<SemesterRoster>().Where(r => rosterRevisions.Contains(r.RevisionId)));
        var questIds = anchors.Select(c => FullDemoOptions.StableId("quest:" + c.Code)).ToArray();
        await ObserveAsync("Admin", admin, admin.Quests.Where(q => questIds.Contains(q.Id)));
        var participantIds = journal.Rows.Where(r => r.Service == "Admin" && r.Table == "QuestParticipants" && r.BeforeJson is null)
            .Select(r => JsonSerializer.Deserialize<JsonElement>(r.KeyJson).GetProperty("Id").GetGuid()).ToArray();
        await ObserveAsync("Admin", admin, admin.QuestParticipants.Where(p => participantIds.Contains(p.Id) && questIds.Contains(p.QuestId)));
        await ObserveAsync("Admin", admin, admin.QuestCompletionRecords.Where(p => participantIds.Contains(p.ParticipantId)));
        await ObserveAsync("Report", reports, reports.Reports.Where(r => reportIds.Contains(r.Id)));
        await ObserveAsync("Export", exports, exports.ExportRequests.Where(e => ExportIds().Contains(e.Id)));
        var exportIds = ExportIds();
        await ObserveAsync("Export", exports, exports.ExportFiles.Where(e => exportIds.Contains(e.ExportRequestId)));
        journal.Checkpoints["receipt-observations"] = "captured"; journal.Write(options.JournalPath);
    }
}
