using ClubReportHub.Shared.Auth;
using AdminService.Data;
using Microsoft.EntityFrameworkCore;

namespace FullDemoDataSeeder;

public sealed partial class FullDemoSeeder
{
    private async Task SeedInvitationsAsync()
    {
        if (journal.Checkpoints.GetValueOrDefault("invitations") == "committed") return;
        for (var index = 0; index < anchors.Count; index++)
        {
            var club = anchors[index];
            var roster = await admin.Set<SemesterRoster>().AsNoTracking().SingleAsync(r => r.SemesterId == semester.Id && r.CampusCode == club.CampusCode);
            var candidates = users.Values.Where(u => u.CampusCode == club.CampusCode && u.Username.StartsWith("fulldemo.student"))
                .OrderByDescending(u => u.Username).Take(2);
            foreach (var candidate in candidates)
            {
                if (await clubs.ClubMemberships.AnyAsync(m => m.ClubId == club.Id && m.UserId == candidate.Id)) continue;
                await CallAsync("POST", $"/api/clubs/{club.Id}/members", Manager(index),
                    new { userId = candidate.Id, fullName = candidate.FullName, role = AuthRoles.ClubMember,
                        semesterId = semester.SemesterCode, expectedRosterVersion = roster.Version }, $"invite-{index}-{candidate.Id}");
            }
        }
        journal.Checkpoints["invitations"] = "committed"; journal.Write(options.JournalPath);
    }
}
