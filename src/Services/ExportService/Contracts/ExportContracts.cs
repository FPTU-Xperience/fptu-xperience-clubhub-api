using System.Text.Json.Serialization;
using ExportService.Models;

namespace ExportService.Contracts;

public sealed record CreateExportRequest(
    [property: JsonPropertyName("exportType")] string? ExportType = null,
    [property: JsonPropertyName("scope")] string? Scope = null,
    [property: JsonPropertyName("period")] string? Period = null,
    [property: JsonPropertyName("clubId")] int? ClubId = null,
    [property: JsonPropertyName("reportId")] int? ReportId = null,
    [property: JsonPropertyName("type")] string? Type = null)
{
    public string ResolvedExportType =>
        ExportTypes.Normalize(ExportType ?? Type) ??
        (string.Equals(Type, "dashboard", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Type, "overview", StringComparison.OrdinalIgnoreCase)
            ? ExportTypes.Excel
            : ExportTypes.Excel);

    public string ResolvedScope =>
        !string.IsNullOrWhiteSpace(Scope)
            ? Scope.Trim()
            : string.Equals(Type, "dashboard", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(Type, "overview", StringComparison.OrdinalIgnoreCase)
                ? "Dashboard"
                : ReportId.HasValue && ReportId.Value > 0
                    ? "Report"
                    : "Dashboard";
}

public sealed record ExportResponse(
    int Id,
    string ExportType,
    string Scope,
    string Status,
    string? Period,
    int? ClubId,
    int? ReportId,
    int RequestedByUserId,
    string RequestedByName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorMessage,
    ExportFileResponse? File,
    bool IsDownloadAvailable = false);

public sealed record ExportFileResponse(
    int Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset ExpiresAtUtc,
    string Checksum,
    bool IsAvailable);
