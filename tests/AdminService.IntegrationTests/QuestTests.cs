using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Contracts;
using Xunit;

namespace AdminService.IntegrationTests;

public sealed class QuestTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetQuests_Anonymous_Returns200WithDefaultQuests()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/quests");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<QuestResponse>>(JsonOptions);
        Assert.NotNull(list);
        Assert.NotEmpty(list);

        // Also verify /api/quests alias
        var aliasResp = await client.GetAsync("/api/quests");
        Assert.Equal(HttpStatusCode.OK, aliasResp.StatusCode);
    }

    [Fact]
    public async Task CreateQuest_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        var request = new CreateQuestRequest(
            Title: "Test Quest",
            Name: null,
            Description: "Test Description",
            Category: "Community",
            Kind: "Cá nhân",
            ReportType: null,
            Tag: null,
            Scope: "Toàn bộ sinh viên",
            RewardXp: 50,
            Reward: null,
            TargetParticipants: 100,
            Target: null,
            SemesterCode: "FALL2026",
            Period: null,
            CampusCode: "GLOBAL",
            Icon: "sparkles",
            Deadline: "2026-12-31",
            DueDate: null,
            Status: "published");

        var response = await client.PostAsJsonAsync("/api/v1/quests", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuest_StudentForbidden_Returns403()
    {
        using var client = CreateClient("student-1");

        var request = new CreateQuestRequest(
            Title: "Test Quest Student",
            Name: null,
            Description: "Test Description",
            Category: "Community",
            Kind: "Cá nhân",
            ReportType: null,
            Tag: null,
            Scope: "Toàn bộ sinh viên",
            RewardXp: 50,
            Reward: null,
            TargetParticipants: 100,
            Target: null,
            SemesterCode: "FALL2026",
            Period: null,
            CampusCode: "GLOBAL",
            Icon: "sparkles",
            Deadline: "2026-12-31",
            DueDate: null,
            Status: "published");

        var response = await client.PostAsJsonAsync("/api/v1/quests", request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ctsv_CanCreateUpdateAndToggleStatusOfQuest()
    {
        using var client = CreateClient("student-affairs");

        // 1. Create a quest
        var createRequest = new CreateQuestRequest(
            Title: "Nhiệm vụ Thử nghiệm Tình nguyện",
            Name: null,
            Description: "Hỗ trợ chuẩn bị sự kiện Ngày hội việc làm 2026.",
            Category: "Community",
            Kind: "Chiến dịch",
            ReportType: null,
            Tag: null,
            Scope: "Toàn bộ sinh viên",
            RewardXp: 35,
            Reward: null,
            TargetParticipants: 50,
            Target: null,
            SemesterCode: "FALL2026",
            Period: null,
            CampusCode: "HAN",
            Icon: "leaf",
            Deadline: "2026-11-20",
            DueDate: null,
            Status: "draft");

        var createResponse = await client.PostAsJsonAsync("/api/v1/quests", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<QuestResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("Nhiệm vụ Thử nghiệm Tình nguyện", created.Title);
        Assert.Equal("draft", created.Status);
        Assert.Equal(35, created.RewardXp);
        Assert.Equal(50, created.TargetParticipants);

        var id = created.Id;

        // 2. Update quest details
        var updateRequest = new UpdateQuestRequest(
            Title: "Nhiệm vụ Tình nguyện Ngày hội Việc làm",
            Name: null,
            Description: "Hỗ trợ đón tiếp doanh nghiệp và sinh viên.",
            Category: null,
            Kind: null,
            ReportType: null,
            Tag: null,
            Scope: null,
            RewardXp: 40,
            Reward: null,
            TargetParticipants: 60,
            Target: null,
            SemesterCode: null,
            Period: null,
            CampusCode: null,
            Icon: null,
            Deadline: null,
            DueDate: null,
            Status: null);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/quests/{id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<QuestResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Nhiệm vụ Tình nguyện Ngày hội Việc làm", updated.Title);
        Assert.Equal(40, updated.RewardXp);
        Assert.Equal(60, updated.TargetParticipants);

        // 3. Toggle status to published
        var patchResponse = await client.PatchAsJsonAsync(
            $"/api/v1/quests/{id}/status",
            new UpdateQuestStatusRequest("published"));
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        var published = await patchResponse.Content.ReadFromJsonAsync<QuestResponse>(JsonOptions);
        Assert.NotNull(published);
        Assert.Equal("published", published.Status);

        // 4. Toggle status to paused
        var pauseResponse = await client.PatchAsJsonAsync(
            $"/api/v1/quests/{id}/status",
            new UpdateQuestStatusRequest("paused"));
        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);

        var paused = await pauseResponse.Content.ReadFromJsonAsync<QuestResponse>(JsonOptions);
        Assert.NotNull(paused);
        Assert.Equal("paused", paused.Status);
    }

    [Fact]
    public async Task Student_CanJoinAndCompleteQuest()
    {
        using var adminClient = CreateClient("admin");
        using var studentClient = CreateClient("student-1");

        // 1. Create a published quest
        var createRequest = new CreateQuestRequest(
            Title: "Học kỳ Quốc tế: Giao lưu Sinh viên Toàn cầu",
            Name: null,
            Description: "Tham gia buổi kết nối văn hóa với sinh viên trao đổi quốc tế.",
            Category: "Global",
            Kind: "Cá nhân",
            ReportType: null,
            Tag: null,
            Scope: "Toàn bộ sinh viên",
            RewardXp: 30,
            Reward: null,
            TargetParticipants: 30,
            Target: null,
            SemesterCode: "FALL2026",
            Period: null,
            CampusCode: "GLOBAL",
            Icon: "sparkles",
            Deadline: "2026-12-15",
            DueDate: null,
            Status: "published");

        var createResponse = await adminClient.PostAsJsonAsync("/api/v1/quests", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var quest = await createResponse.Content.ReadFromJsonAsync<QuestResponse>(JsonOptions);
        Assert.NotNull(quest);
        var questId = quest.Id;

        // 2. Student joins quest
        var joinResponse = await studentClient.PostAsJsonAsync(
            $"/api/v1/quests/{questId}/join",
            new JoinQuestRequest("Em xin đăng ký tham gia."));
        Assert.Equal(HttpStatusCode.Created, joinResponse.StatusCode);

        var participant = await joinResponse.Content.ReadFromJsonAsync<QuestParticipantResponse>(JsonOptions);
        Assert.NotNull(participant);
        Assert.Equal("Joined", participant.Status);
        Assert.Equal(questId, participant.QuestId);

        // 3. Duplicate join is idempotent
        var dupResponse = await studentClient.PostAsJsonAsync(
            $"/api/v1/quests/{questId}/join",
            new JoinQuestRequest(null));
        Assert.Equal(HttpStatusCode.OK, dupResponse.StatusCode);

        // 4. Student completes quest
        var completeResponse = await studentClient.PostAsJsonAsync(
            $"/api/v1/quests/{questId}/complete",
            new CompleteQuestRequest(null, "Đã hoàn thành xuất sắc."));
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        var completedPart = await completeResponse.Content.ReadFromJsonAsync<QuestParticipantResponse>(JsonOptions);
        Assert.NotNull(completedPart);
        Assert.Equal("Completed", completedPart.Status);

        // 5. Verify quest progress updated
        var getQuest = await studentClient.GetAsync($"/api/v1/quests/{questId}");
        var questUpdated = await getQuest.Content.ReadFromJsonAsync<QuestResponse>(JsonOptions);
        Assert.NotNull(questUpdated);
        Assert.Equal(1, questUpdated.JoinedCount);
        Assert.Equal(1, questUpdated.CompletedCount);
        Assert.True(questUpdated.ProgressPercent > 0);

        // 6. Cannot delete quest that has completed participants
        var delResponse = await adminClient.DeleteAsync($"/api/v1/quests/{questId}");
        Assert.Equal(HttpStatusCode.BadRequest, delResponse.StatusCode);

        // 7. Student checks /api/v1/quests/me
        var myQuestsResp = await studentClient.GetAsync("/api/v1/quests/me");
        Assert.Equal(HttpStatusCode.OK, myQuestsResp.StatusCode);
        var myQuests = await myQuestsResp.Content.ReadFromJsonAsync<List<QuestParticipantResponse>>(JsonOptions);
        Assert.NotNull(myQuests);
        Assert.Contains(myQuests, p => p.QuestId == questId && p.Status == "Completed");
    }

    private HttpClient CreateClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
