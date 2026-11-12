using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ClubReportHub.Shared.Experience;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DiscoveryTagSelection(IReadOnlyList<string>? Experience = null,
    string? ActivityCategory = null, IReadOnlyList<string>? Custom = null);

public sealed record DiscoveryTag(string Id, string Label, string Kind);

public static class DiscoveryTags
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex CustomLabel = new(@"\A[\p{L}\p{N}][\p{L}\p{M}\p{N} .&+#/_-]{0,39}\z");
    private static readonly IReadOnlyDictionary<string, string> ActivityCategories = new Dictionary<string, string>
    {
        ["workshop"] = "Workshop",
        ["competition"] = "Cuộc thi",
        ["talk"] = "Hội thảo",
        ["volunteering"] = "Tình nguyện",
        ["networking"] = "Giao lưu",
        ["performance"] = "Biểu diễn",
        ["sports"] = "Thể thao",
        ["other"] = "Hoạt động khác"
    };
    private static readonly IReadOnlyDictionary<string, string> ClubCategories = new Dictionary<string, string>
    {
        ["ACADEMIC"] = "Học thuật",
        ["TECHNOLOGY"] = "Công nghệ",
        ["ARTS"] = "Nghệ thuật",
        ["SPORTS"] = "Thể thao",
        ["VOLUNTEER"] = "Cộng đồng",
        ["OTHER"] = "Khác"
    };

    public const string InvalidMessage = "Use recognized experience and category codes and up to 10 distinct custom tags of 1–40 characters.";
    private static readonly string OtherClubCategoryId = "club-category:" + Key("OTHER");

    public static bool IsAffinityTag(string id) => id != "activity-category:other" && id != OtherClubCategoryId;

    public static bool TryNormalize(DiscoveryTagSelection? input, bool activity, out DiscoveryTagSelection normalized)
    {
        normalized = new();
        if (input is null) return true;
        var experience = input.Experience ?? [];
        var custom = input.Custom ?? [];
        if (experience.Count > RadarPillars.AllSeven.Length || custom.Count > 10
            || experience.Any(x => !RadarPillars.AllSeven.Contains(x, StringComparer.Ordinal))
            || experience.Distinct(StringComparer.Ordinal).Count() != experience.Count
            || input.ActivityCategory is { } category && (!activity || !ActivityCategories.ContainsKey(category)))
            return false;
        var labels = new List<string>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in custom)
        {
            if (label is null || label.Length > 100 || label.Any(char.IsControl)) return false;
            var clean = string.Join(' ', label.Normalize().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (!CustomLabel.IsMatch(clean) || !keys.Add(clean)) return false;
            labels.Add(clean);
        }
        normalized = new(RadarPillars.AllSeven.Where(experience.Contains).ToArray(),
            input.ActivityCategory, labels.ToArray());
        return true;
    }

    public static string Store(DiscoveryTagSelection selection) => JsonSerializer.Serialize(selection, Json);
    public static DiscoveryTagSelection Read(string? json) =>
        json is null ? new() : JsonSerializer.Deserialize<DiscoveryTagSelection>(json, Json) ?? new();

    public static IReadOnlyList<DiscoveryTag> ForClub(string? json, string category)
    {
        var selection = Read(json);
        var code = category.ToUpperInvariant();
        return [
            new("club-category:" + Key(code), ClubCategories.GetValueOrDefault(code, category), "category"),
            .. Details(selection, [])
        ];
    }

    public static IReadOnlyList<DiscoveryTag> ForActivity(string? json, IReadOnlyList<PillarWeight> weights)
    {
        var selection = Read(json);
        var category = selection.ActivityCategory ?? "other";
        return [
            new("activity-category:" + category, ActivityCategories[category], "category"),
            .. Details(selection, weights.Where(x => x.Weight > 0).Select(x => x.Pillar))
        ];
    }

    private static IEnumerable<DiscoveryTag> Details(DiscoveryTagSelection selection, IEnumerable<string> assigned)
    {
        var experience = (selection.Experience ?? []).Concat(assigned).ToHashSet(StringComparer.Ordinal);
        return RadarPillars.AllSeven.Where(experience.Contains)
            .Select(x => new DiscoveryTag("experience:" + x, RadarPillars.GetVietnameseName(x), "experience"))
            .Concat((selection.Custom ?? []).Select(x => new DiscoveryTag("custom:" + Key(x), x, "custom")));
    }

    private static string Key(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()))).ToLowerInvariant();
}
