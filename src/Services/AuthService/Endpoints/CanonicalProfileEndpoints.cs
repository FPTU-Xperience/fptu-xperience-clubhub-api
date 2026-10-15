using System.Text.Json;
using AuthService.Data;
using AuthService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Endpoints;

public static class CanonicalProfileEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static void MapCanonicalProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users/me/profile", async (AuthDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == http.User.GetUserId(), ct);
            if (user is null) return Results.NotFound();
            var profile = await db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
            return Results.Ok(Response(user, profile));
        }).RequireAuthorization(AuthPolicies.AllActors);
        app.MapPatch("/api/users/me/profile", PatchAsync).RequireAuthorization(AuthPolicies.AllActors);
        app.MapPost("/api/internal/experience/user-profiles", async (int[] userIds, AuthDbContext db, HttpContext http, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            if (userIds.Length is 0 or > 500 || userIds.Distinct().Count() != userIds.Length) return Results.BadRequest();
            var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToListAsync(ct);
            var profiles = await db.UserProfiles.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, ct);
            return Results.Ok(users.Select(u => new
            {
                u.Id,
                u.FullName,
                u.Email,
                u.CampusCode,
                u.IsActive,
                u.IsLocked,
                profile = profiles.TryGetValue(u.Id, out var p) ? Response(u, p) : null
            }));
        }).AllowAnonymous().ExcludeFromDescription();
    }

    private static object Response(User user, CanonicalUserProfile? profile) => new
    {
        userId = user.Id,
        version = profile?.Version ?? Guid.Empty,
        profileSource = "user-profile",
        identityProvenance = "PROJECT_ACCOUNT",
        profile = new
        {
            displayName = profile?.DisplayName ?? user.FullName,
            about = profile?.About ?? "",
            skills = JsonSerializer.Deserialize<string[]>(profile?.SkillsJson ?? "[]"),
            interests = JsonSerializer.Deserialize<string[]>(profile?.InterestsJson ?? "[]")
        },
        personal = new { phoneNumber = profile?.PhoneNumber },
        editableFields = new[] { "profile.displayName", "profile.about", "profile.skills", "profile.interests", "personal.phoneNumber" }
    };

    private static async Task<IResult> PatchAsync(JsonElement request, AuthDbContext db, HttpContext http, CancellationToken ct)
    {
        if (request.ValueKind != JsonValueKind.Object || request.EnumerateObject().Any(x => x.Name is not ("expectedVersion" or "profile" or "personal"))
            || request.EnumerateObject().GroupBy(x => x.Name).Any(x => x.Count() > 1)
            || !request.TryGetProperty("expectedVersion", out var version) || version.ValueKind != JsonValueKind.String || !version.TryGetGuid(out var expected))
            return ApiOperations.Error(400, "INVALID_SELF_PROFILE", "Provide editable profile/personal fields and expectedVersion.");
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == http.User.GetUserId(), ct);
        if (user is null || !user.IsActive || user.IsLocked) return Results.Forbid();
        return await ApiOperations.ExecuteAsync(http, db, request, async () =>
        {
            var row = await db.UserProfiles.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
            if ((row?.Version ?? Guid.Empty) != expected) return ApiOperations.Error(409, "STALE_PROFILE", "Reload your profile.");
            if (row is null) { row = new CanonicalUserProfile { UserId = user.Id, DisplayName = user.FullName }; db.UserProfiles.Add(row); }
            var changed = false;
            foreach (var section in request.EnumerateObject().Where(x => x.Name != "expectedVersion"))
            {
                if (section.Value.ValueKind != JsonValueKind.Object || section.Value.EnumerateObject().GroupBy(x => x.Name).Any(x => x.Count() > 1)) return Results.BadRequest();
                foreach (var field in section.Value.EnumerateObject())
                {
                    var path = section.Name + "." + field.Name;
                    if (path is "profile.skills" or "profile.interests")
                    {
                        if (field.Value.ValueKind != JsonValueKind.Array || field.Value.GetArrayLength() > 30
                            || field.Value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(x.GetString()) || x.GetString()!.Length > 100)) return Results.BadRequest();
                        var json = JsonSerializer.Serialize(field.Value.EnumerateArray().Select(x => x.GetString()!.Trim()).Distinct().ToArray());
                        if (field.Name == "skills") row.SkillsJson = json; else row.InterestsJson = json;
                    }
                    else
                    {
                        if (path is not ("profile.displayName" or "profile.about" or "personal.phoneNumber")) return ApiOperations.Error(403, "PROFILE_FIELD_FORBIDDEN", "Identity, roles and account state are read-only.");
                        if (field.Value.ValueKind != JsonValueKind.String && !(path == "personal.phoneNumber" && field.Value.ValueKind == JsonValueKind.Null)) return Results.BadRequest();
                        var text = field.Value.ValueKind == JsonValueKind.Null ? null : field.Value.GetString()!.Trim();
                        if (path == "profile.displayName" && (string.IsNullOrWhiteSpace(text) || text.Length > 120)
                            || path == "profile.about" && text?.Length > 2000
                            || path == "personal.phoneNumber" && text is not null && (text.Length > 20 || text.Any(c => !char.IsAsciiDigit(c) && c is not ('+' or ' ' or '-' or '(' or ')')))) return Results.BadRequest();
                        if (path == "profile.displayName") row.DisplayName = text!; else if (path == "profile.about") row.About = text!; else row.PhoneNumber = text;
                    }
                    changed = true;
                }
            }
            if (!changed) return ApiOperations.Error(400, "EMPTY_PATCH", "Provide an editable field.");
            row.Version = Guid.NewGuid(); row.UpdatedAtUtc = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
            return Results.Ok(Response(user, row));
        }, ct, required: false);
    }
}
