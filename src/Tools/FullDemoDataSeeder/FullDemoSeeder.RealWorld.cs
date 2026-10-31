using System.Text.Json;
using AdminService.Contracts;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace FullDemoDataSeeder;

public sealed partial class FullDemoSeeder
{
    private async Task SeedRealWorldExperienceAsync()
    {
        if (journal.Checkpoints.GetValueOrDefault("real-world") == "committed") return;
        var reviewer = users["admin01"];
        if (!await admin.VerificationAssignments.AnyAsync(g => g.UserId == reviewer.Id && g.SourceType == "REAL_WORLD" && !g.IsRevoked))
            await CallAsync("POST", "/api/verification-assignments/", reviewer,
                new CreateVerificationAssignmentRequest(reviewer.Id, "REAL_WORLD", null, ["contribution.review"], options.At(-80),
                    semester.EndDate!.Value.AddDays(30), "[FullDemo v2] Xác minh minh chứng thực hành nghề nghiệp mô phỏng."), "grant-real-world");
        for (var index = 0; index < anchors.Count; index++)
        {
            var author = (await MembersAsync(anchors[index])).First();
            var evidence = await EvidenceAsync(author, "real-world-" + index, index + 1);
            var attributes = new ContributionAttributes(index % 2 == 0 ? "T3" : "T4", "Product", "Campus", "RealWorldOutput", [new("RealWorldWork", 1m)]);
            var submitted = await CallAsync("POST", "/api/v1/declarations/", author,
                new SubmitDeclarationRequest($"[Demo] Dự án thực hành nghề nghiệp {index + 1:00}", "RealWorldWork",
                    "Đơn vị thực hành mô phỏng ClubHub", true, null, "", "[Demo] Hoàn thành sản phẩm nhóm và trình bày kết quả thực hành.",
                    "Product", options.ReferenceDate.AddDays(-10), options.ReferenceDate.AddDays(-5), author.CampusCode,
                    semester.SemesterCode, options.At(-5), attributes, [evidence], PolicyVersion: periodPolicy.PolicyVersion), "real-world-" + index + "-submit");
            await CallAsync("POST", $"/api/contributions/{submitted.GetProperty("id").GetGuid()}/reviews", reviewer,
                new ReviewContributionRequest("APPROVE", "[Demo] Đối chiếu sản phẩm thực hành và minh chứng mô phỏng.",
                    submitted.GetProperty("version").GetGuid(), attributes), "real-world-" + index + "-review");
        }
        journal.Checkpoints["real-world"] = "committed"; journal.Write(options.JournalPath);
    }

    private async Task SeedAdjustmentExampleAsync()
    {
        if (journal.Checkpoints.GetValueOrDefault("adjustment-example") == "committed") return;
        var club = anchors[0]; var manager = Manager(0);
        if (!await admin.VerificationAssignments.AnyAsync(g => g.UserId == manager.Id && g.ClubId == club.Id && !g.IsRevoked && g.CapabilitiesJson.Contains("experience.adjust")))
            await CallAsync("POST", "/api/verification-assignments/", users["admin01"],
                new CreateVerificationAssignmentRequest(manager.Id, "CLUB", club.Id, ["experience.adjust"], options.At(-80),
                    semester.EndDate!.Value.AddDays(30), "[FullDemo v2] Minh họa thu hồi có kiểm soát một đóng góp mô phỏng."), "grant-adjustment-example");
        var source = JsonSerializer.Deserialize<JsonElement>(journal.Checkpoints["api:contribution-0-0-0-submit"]);
        var id = source.GetProperty("id").GetGuid();
        var contribution = await admin.Contributions.AsNoTracking().SingleAsync(c => c.Id == id);
        if (contribution.Status == "APPROVED")
            await CallAsync("POST", $"/api/contributions/{id}/adjustments", manager,
                new AdjustContributionRequest("REVOKE", "[Demo] Thu hồi đúng một hồ sơ để minh họa hoàn tác bằng bút toán đảo, giữ nguyên các hồ sơ khác.", contribution.Version), "adjustment-example");
        journal.Checkpoints["adjustment-example"] = "committed"; journal.Write(options.JournalPath);
    }
}
