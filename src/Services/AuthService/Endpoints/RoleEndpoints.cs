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
        roles.MapPost("/", HandleCreateRole)
            .RequireAuthorization(AuthPolicies.SystemAdministration);

        return app;
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
