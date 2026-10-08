using AuthService.Contracts;
using AuthService.Data;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Endpoints;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var roles = app.MapGroup("/api/roles")
            .WithTags("Roles")
            .RequireAuthorization(AuthPolicies.UserDirectoryRead);

        roles.MapGet("/", HandleGetRoles);
        roles.MapGet("/stats", HandleGetRoleStats);
        roles.MapPost("/", HandleCreateRole)
            .RequireAuthorization(AuthPolicies.SystemAdministration);

        return app;
    }

    public static async Task<IResult> HandleGetRoleStats(
        string? campus,
        AuthDbContext db,
        CancellationToken cancellationToken)
    {
        var usersQuery = db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(campus))
        {
            var normalizedCampus = CampusCodes.Normalize(campus);
            if (normalizedCampus != CampusCodes.Global)
            {
                usersQuery = usersQuery.Where(u => u.CampusCode == normalizedCampus);
            }
        }

        var totalUsers = await usersQuery.CountAsync(cancellationToken);
        var activeUsers = await usersQuery.CountAsync(u => u.IsActive && !u.IsLocked, cancellationToken);
        var lockedUsers = await usersQuery.CountAsync(u => u.IsLocked, cancellationToken);

        var userRolesQuery = db.UserRoles.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(campus))
        {
            var normalizedCampus = CampusCodes.Normalize(campus);
            if (normalizedCampus != CampusCodes.Global)
            {
                userRolesQuery = userRolesQuery.Where(ur => ur.User.CampusCode == normalizedCampus);
            }
        }

        var counts = await userRolesQuery
            .GroupBy(ur => ur.Role.Name)
            .Select(g => new { RoleName = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byRole = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["ADMIN"] = 0,
            ["STUDENT_AFFAIRS_ADMIN"] = 0,
            ["CLUB_MANAGER"] = 0,
            ["CLUB_MEMBER"] = 0
        };

        foreach (var c in counts)
        {
            byRole[c.RoleName] = c.Count;
        }

        var items = byRole
            .Select(kv => new RoleStatItem(kv.Key, kv.Value))
            .OrderBy(x => x.Role)
            .ToList();

        var response = RoleStatsResponse.Create(
            totalUsers: totalUsers,
            activeUsers: activeUsers,
            lockedUsers: lockedUsers,
            byRole: byRole,
            items: items);

        return Results.Ok(response);
    }

    private static async Task<IResult> HandleGetRoles(AuthDbContext db)
    {
        var roles = await db.Roles
            .OrderBy(x => x.Name)
            .ToListAsync();

        return Results.Ok(roles);
    }

    private static async Task<IResult> HandleCreateRole(
        CreateRoleRequest request,
        AuthDbContext db)
    {
        var roleName = request.Name.Trim().ToUpperInvariant();

        // Google-only demo accounts are deliberately limited to the three
        // actors that the teacher is expected to test.
        if (!ActorAccountPolicy.IsAllowedGoogleActorRole(roleName))
        {
            return Results.BadRequest(new { message = "Only ADMIN, STUDENT_AFFAIRS_ADMIN, CLUB_MANAGER, and CLUB_MEMBER roles are enabled for Google sign-in." });
        }

        // Check if already exists
        if (await db.Roles.AnyAsync(x => x.Name == roleName))
        {
            return Results.Conflict(new { message = "Role already exists." });
        }

        var role = new Models.Role { Name = roleName };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        return Results.Created($"/api/roles/{role.Id}", role);
    }
}
