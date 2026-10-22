using AuthService.Data;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Rosters;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Endpoints;

public static class SchoolAccountEndpoints
{
    public static void MapSchoolAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/internal/semester-roster/accounts/resolve", async (HttpContext http, AuthDbContext db, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!VerifiedExperienceRules.InternalRequest(http, configuration)) return Results.Forbid();
            var input = await BoundedJsonRequest.ReadAsync<SchoolAccountQuery>(http, 256 * 1024, ct);
            var byEmail = input.Emails is not null;
            RosterErrors.Check(byEmail != (input.UserIds is not null)
                && (byEmail ? input.Emails!.Length is > 0 and <= 500 && input.Emails.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 200)
                    : input.UserIds!.Length is > 0 and <= 500 && input.UserIds.All(x => x > 0)),
                400, "INVALID_ACCOUNT_QUERY", "Resolve one to 500 emails or user IDs.");
            var emails = input.Emails?.Select(x => x.Trim().ToLowerInvariant()).ToArray() ?? [];
            var ids = input.UserIds ?? [];
            var users = await db.Users.AsNoTracking().Include(x => x.UserRoles).ThenInclude(x => x.Role)
                .Where(x => byEmail ? emails.Contains(x.Email.ToLower()) : ids.Contains(x.Id)).ToArrayAsync(ct);
            var userIds = users.Select(x => x.Id).ToArray();
            var profiles = await db.UserProfiles.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, ct);
            var domains = (configuration["Roster:SchoolEmailDomains"] ?? "fpt.edu.vn").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return Results.Ok(users.Select(x => new SchoolAccount(x.Id, x.Email.Trim().ToLowerInvariant(),
                profiles.TryGetValue(x.Id, out var profile) && !string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.DisplayName : x.FullName,
                x.CampusCode, x.IsActive, x.IsLocked, ActorAccountPolicy.HasValidActorConfiguration(x)
                    && x.UserRoles.Any(r => r.Role.Name is AuthRoles.ClubMember or AuthRoles.ClubManager),
                domains.Contains(x.Email.Split('@').Last(), StringComparer.OrdinalIgnoreCase))));
        }).AllowAnonymous().ExcludeFromDescription().AddEndpointFilter<RosterExceptionFilter>();
    }
}
