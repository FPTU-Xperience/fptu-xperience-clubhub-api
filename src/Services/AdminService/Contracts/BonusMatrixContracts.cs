using System.Text.Json.Serialization;
using AdminService.Data;

namespace AdminService.Contracts;

public sealed record BonusMatrixNodeResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("multiplier")] int Multiplier,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt)
{
    public static BonusMatrixNodeResponse From(BonusMatrixNode node) =>
        new(
            Id: node.Id.ToString(),
            Position: node.Position,
            Label: node.Label,
            Multiplier: node.Multiplier,
            CreatedAt: node.CreatedAtUtc,
            UpdatedAt: node.UpdatedAtUtc);
}

public sealed record CreateBonusMatrixNodeRequest(
    [property: JsonPropertyName("position")] int? Position,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("multiplier")] int? Multiplier);

public sealed record UpdateBonusMatrixNodeRequest(
    [property: JsonPropertyName("position")] int? Position,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("multiplier")] int? Multiplier);

public sealed record BonusMatrixSuccessResponse<T>(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("data")] T Data);

public sealed record BonusMatrixMessageResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message);

public sealed record BonusMatrixErrorDetails(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

public sealed record BonusMatrixErrorResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] BonusMatrixErrorDetails Error);
