using System.Text.Json.Serialization;
using AdminService.Data;

namespace AdminService.Contracts;

public sealed record AnomalyResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("club")] string Club,
    [property: JsonPropertyName("student")] string Student,
    [property: JsonPropertyName("studentUserId")] int StudentUserId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("evidence")] string Evidence,
    [property: JsonPropertyName("amount")] int Amount,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("semesterCode")] string SemesterCode,
    [property: JsonPropertyName("campusCode")] string CampusCode,
    [property: JsonPropertyName("decision")] string? Decision,
    [property: JsonPropertyName("adjustment")] int? Adjustment,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("resolvedByName")] string? ResolvedByName,
    [property: JsonPropertyName("resolvedAtUtc")] DateTimeOffset? ResolvedAtUtc,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc)
{
    public static AnomalyResponse From(XpAnomaly a)
    {
        var timeStr = a.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm");
        return new(
            Id: a.Id.ToString(),
            Title: a.Title,
            Severity: a.Severity,
            Status: a.Status,
            Club: a.Club,
            Student: a.Student,
            StudentUserId: a.StudentUserId,
            Source: a.Source,
            Evidence: a.Evidence,
            Amount: a.Amount,
            Count: a.Count,
            Time: timeStr,
            SemesterCode: a.SemesterCode,
            CampusCode: a.CampusCode,
            Decision: a.Decision,
            Adjustment: a.Adjustment,
            Reason: a.Reason,
            ResolvedByName: a.ResolvedByName,
            ResolvedAtUtc: a.ResolvedAtUtc,
            CreatedAtUtc: a.CreatedAtUtc);
    }
}

public sealed record ResolveAnomalyRequest(
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("adjustment")] int? Adjustment,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record XpLedgerEntryResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("studentUserId")] int StudentUserId,
    [property: JsonPropertyName("studentName")] string StudentName,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("amount")] int Amount,
    [property: JsonPropertyName("rubricVersion")] int RubricVersion,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("pillarCategory")] string PillarCategory,
    [property: JsonPropertyName("semesterCode")] string SemesterCode,
    [property: JsonPropertyName("campusCode")] string CampusCode,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc)
{
    public static XpLedgerEntryResponse From(XpLedgerEntry l) =>
        new(
            Id: l.Id.ToString(),
            StudentUserId: l.StudentUserId,
            StudentName: l.StudentName,
            Type: l.Type,
            Source: l.Source,
            Actor: l.Actor,
            Amount: l.Amount,
            RubricVersion: l.RubricVersion,
            Reason: l.Reason,
            PillarCategory: l.PillarCategory,
            SemesterCode: l.SemesterCode,
            CampusCode: l.CampusCode,
            CreatedAtUtc: l.CreatedAtUtc);
}

public sealed record AnomalyStatsResponse(
    [property: JsonPropertyName("openCount")] int OpenCount,
    [property: JsonPropertyName("highSeverityCount")] int HighSeverityCount,
    [property: JsonPropertyName("resolvedCount")] int ResolvedCount,
    [property: JsonPropertyName("totalLedgerCount")] int TotalLedgerCount);
