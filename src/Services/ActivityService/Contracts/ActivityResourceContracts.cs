using System.Text.Json;
using System.Text.Json.Serialization;

namespace ActivityService.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivityResourceCreate(string? Kind, string? Title, string? Visibility, string? Url,
    int? Position = null, Guid? UploadIntentId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivityResourcePatch(Guid ExpectedVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Title = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Visibility = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Position = default);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivityResourceDetach(Guid ExpectedVersion, string? Reason);

public sealed record ActivityResourceCapabilities(bool CanManage, bool CanUpload, IReadOnlyList<string> SupportedKinds,
    string UploadStatus = "NOT_IMPLEMENTED");
public sealed record ActivityResourceResponse(Guid Id, string Kind, string Title, string Visibility, string Url,
    string Provider, int Position, Guid Version, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UploadedByUserId { get; init; }
}
public sealed record ActivityResourceGallery(IReadOnlyList<ActivityResourceResponse> Items,
    ActivityResourceCapabilities ResourceCapabilities, string Availability = "AVAILABLE");
