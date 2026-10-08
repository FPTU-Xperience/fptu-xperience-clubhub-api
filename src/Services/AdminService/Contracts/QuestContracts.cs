using System.Text.Json.Serialization;
using AdminService.Data;

namespace AdminService.Contracts;

public sealed record QuestResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("rewardXp")] int RewardXp,
    [property: JsonPropertyName("reward")] int Reward,
    [property: JsonPropertyName("targetParticipants")] int TargetParticipants,
    [property: JsonPropertyName("target")] int Target,
    [property: JsonPropertyName("semesterCode")] string SemesterCode,
    [property: JsonPropertyName("period")] string Period,
    [property: JsonPropertyName("season")] string Season,
    [property: JsonPropertyName("campusCode")] string CampusCode,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("deadline")] string? Deadline,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("joinedCount")] int JoinedCount,
    [property: JsonPropertyName("joined")] int Joined,
    [property: JsonPropertyName("completedCount")] int CompletedCount,
    [property: JsonPropertyName("completed")] int Completed,
    [property: JsonPropertyName("progressPercent")] double ProgressPercent,
    [property: JsonPropertyName("createdByName")] string? CreatedByName,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc)
{
    public static QuestResponse From(Quest quest, int joinedCount = 0, int completedCount = 0)
    {
        var target = Math.Max(1, quest.TargetParticipants);
        var progress = Math.Round((double)joinedCount / target * 100, 1);
        var deadlineString = quest.DeadlineUtc?.ToString("yyyy-MM-dd");

        return new(
            Id: quest.Id.ToString(),
            Title: quest.Title,
            Name: quest.Title,
            Description: quest.Description,
            Category: quest.Category,
            Kind: quest.Kind,
            Scope: quest.Scope,
            RewardXp: quest.RewardXp,
            Reward: quest.RewardXp,
            TargetParticipants: quest.TargetParticipants,
            Target: quest.TargetParticipants,
            SemesterCode: quest.SemesterCode,
            Period: quest.SemesterCode,
            Season: quest.SemesterCode,
            CampusCode: quest.CampusCode,
            Icon: quest.Icon,
            Deadline: deadlineString,
            Status: quest.Status,
            JoinedCount: joinedCount,
            Joined: joinedCount,
            CompletedCount: completedCount,
            Completed: completedCount,
            ProgressPercent: progress,
            CreatedByName: quest.CreatedByName,
            CreatedAtUtc: quest.CreatedAtUtc,
            UpdatedAtUtc: quest.UpdatedAtUtc);
    }
}

public sealed record CreateQuestRequest(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("reportType")] string? ReportType,
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("rewardXp")] int? RewardXp,
    [property: JsonPropertyName("reward")] int? Reward,
    [property: JsonPropertyName("targetParticipants")] int? TargetParticipants,
    [property: JsonPropertyName("target")] int? Target,
    [property: JsonPropertyName("semesterCode")] string? SemesterCode,
    [property: JsonPropertyName("period")] string? Period,
    [property: JsonPropertyName("campusCode")] string? CampusCode,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("deadline")] string? Deadline,
    [property: JsonPropertyName("dueDate")] string? DueDate,
    [property: JsonPropertyName("status")] string? Status);

public sealed record UpdateQuestRequest(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("reportType")] string? ReportType,
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("rewardXp")] int? RewardXp,
    [property: JsonPropertyName("reward")] int? Reward,
    [property: JsonPropertyName("targetParticipants")] int? TargetParticipants,
    [property: JsonPropertyName("target")] int? Target,
    [property: JsonPropertyName("semesterCode")] string? SemesterCode,
    [property: JsonPropertyName("period")] string? Period,
    [property: JsonPropertyName("campusCode")] string? CampusCode,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("deadline")] string? Deadline,
    [property: JsonPropertyName("dueDate")] string? DueDate,
    [property: JsonPropertyName("status")] string? Status);

public sealed record UpdateQuestStatusRequest(
    [property: JsonPropertyName("status")] string Status);

public sealed record JoinQuestRequest(
    [property: JsonPropertyName("note")] string? Note);

public sealed record CompleteQuestRequest(
    [property: JsonPropertyName("studentUserId")] int? StudentUserId,
    [property: JsonPropertyName("note")] string? Note);

public sealed record QuestParticipantResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("questId")] string QuestId,
    [property: JsonPropertyName("studentUserId")] int StudentUserId,
    [property: JsonPropertyName("studentName")] string StudentName,
    [property: JsonPropertyName("studentEmail")] string StudentEmail,
    [property: JsonPropertyName("campusCode")] string CampusCode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("joinedAtUtc")] DateTimeOffset JoinedAtUtc,
    [property: JsonPropertyName("completedAtUtc")] DateTimeOffset? CompletedAtUtc,
    [property: JsonPropertyName("verifiedByName")] string? VerifiedByName,
    [property: JsonPropertyName("note")] string? Note)
{
    public static QuestParticipantResponse From(QuestParticipant p) =>
        new(
            Id: p.Id.ToString(),
            QuestId: p.QuestId.ToString(),
            StudentUserId: p.StudentUserId,
            StudentName: p.StudentName,
            StudentEmail: p.StudentEmail,
            CampusCode: p.CampusCode,
            Status: p.Status,
            JoinedAtUtc: p.JoinedAtUtc,
            CompletedAtUtc: p.CompletedAtUtc,
            VerifiedByName: p.VerifiedByName,
            Note: p.Note);
}
