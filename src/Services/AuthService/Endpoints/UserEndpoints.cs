using System.Security.Claims;
using System.Text.Json;
using AuthService.Contracts;
using AuthService.Data;
using AuthService.Models;
using AuthService.Services;
using AuthService.Validators;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AuthService.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // Self-service must not inherit the management-only group policy.
        app.MapGet("/api/users/me", HandleGetCurrentUser)
            .WithTags("Users")
            .RequireAuthorization(AuthPolicies.AllActors);

        var users = app.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization(AuthPolicies.UserDirectoryRead);

        users.MapGet("/", HandleGetUsers);
        users.MapGet("/{id:int}", HandleGetUser);
        users.MapPost("/", HandleCreateUser).RequireAuthorization(AuthPolicies.SystemAdministration);
        users.MapPut("/{id:int}", HandleUpdateUser).RequireAuthorization(AuthPolicies.SystemAdministration);
        users.MapDelete("/{id:int}", HandleDisableUser).RequireAuthorization(AuthPolicies.SystemAdministration);
        users.MapPost("/{id:int}/roles", HandleAssignRole).RequireAuthorization(AuthPolicies.SystemAdministration);
        users.MapDelete("/{id:int}/roles/{roleId}", HandleRemoveRole).RequireAuthorization(AuthPolicies.SystemAdministration);
        users.MapPatch("/{id:int}/lock", HandleLockUser).RequireAuthorization(AuthPolicies.SystemAdministration);
        users.MapPatch("/{id:int}/unlock", HandleUnlockUser).RequireAuthorization(AuthPolicies.SystemAdministration);

        return app;
    }

    private static Task<IResult> HandleGetCurrentUser(
        ClaimsPrincipal user,
        AuthDbContext db,
        CancellationToken cancellationToken)
        => HandleGetUser(user.GetUserId(), db, cancellationToken);

    private static async Task<IResult> HandleGetUser(
        int id,
        AuthDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return user is null ? Results.NotFound() : Results.Ok(ToSummary(user));
    }

    private static async Task<IResult> HandleGetUsers(
        string? search,
        int? page,
        int? pageSize,
        AuthDbContext db,
        CancellationToken cancellationToken)
    {
        var resolvedPage = Math.Max(page ?? 1, 1);
        var resolvedPageSize = Math.Clamp(pageSize ?? 20, 1, 500);

        var query = db.Users.AsNoTracking().AsQueryable();

        // Search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.Username.Contains(term) ||
                x.FullName.Contains(term) ||
                x.Email.Contains(term) ||
                x.UserRoles.Any(userRole => userRole.Role.Name.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var usersResult = await query
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            items = usersResult.Select(u => UserEndpoints.ToSummary(u)),
            total,
            page = resolvedPage,
            pageSize = resolvedPageSize
        });
    }

    private static async Task<IResult> HandleCreateUser(
        CreateUserRequest request,
        AuthDbContext db,
        ClaimsPrincipal actor,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AuthService.UserManagement");

        // Validate input
        var validation = AuthValidators.ValidateCreateUser(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new { errors = validation.Errors.ToDictionary(e => e) });
        }

        var username = request.Username.Trim();
        var email = NormalizeEmail(request.Email);

        // E-mail is the Google allow-list key; username remains an internal
        // display/reference field and is never accepted for authentication.
        if (await db.Users.AnyAsync(x =>
            x.Username == username || x.Email.ToLower() == email))
        {
            return Results.Conflict(new { message = "Username or email already exists." });
        }

        // Validate role
        var requestedRoleNames = request.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requestedRoleNames.Length != 1 ||
            !ActorAccountPolicy.IsAllowedGoogleActorRole(requestedRoleNames[0]))
        {
            return Results.BadRequest(new { message = "Each account must have exactly one Google-enabled actor role: ADMIN, CLUB_MANAGER, or CLUB_MEMBER." });
        }

        // Only ADMIN can create another ADMIN
        if (requestedRoleNames[0] == AuthRoles.Admin && !actor.IsInRole(AuthRoles.Admin))
        {
            return Results.Forbid();
        }

        // Get role from DB
        var roles = await db.Roles
            .Where(x => requestedRoleNames.Contains(x.Name))
            .ToListAsync();

        if (roles.Count != 1)
        {
            return Results.BadRequest(new { message = "The requested actor role is not available." });
        }

        // Create user
        var user = new User
        {
            Username = username,
            FullName = request.FullName.Trim(),
            Email = email,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserRoles.AddRange(roles.Select(role =>
            new UserRole { UserId = user.Id, RoleId = role.Id }));
        await db.SaveChangesAsync();

        logger.LogInformation("User created: {Username}, Email: {Email}, Roles: {Roles}, ActorId: {ActorId}",
            user.Username, user.Email, string.Join(",", roles.Select(r => r.Name)), actor.GetUserId());

        return Results.Created($"/api/users/{user.Id}", ToSummary(user, roles.Select(x => x.Name)));
    }

    private static async Task<IResult> HandleUpdateUser(
        int id,
        UpdateUserRequest request,
        AuthDbContext db,
        ClaimsPrincipal actor,
        RefreshTokenService refreshTokenService,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AuthService.UserManagement");

        // Validate input
        var validation = AuthValidators.ValidateUpdateUser(request);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new { errors = validation.Errors.ToDictionary(e => e) });
        }

        // Find user
        var user = await db.Users
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (user is null)
        {
            return Results.NotFound();
        }

        // Check email duplicate
        var trimmedEmail = NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(x => x.Id != id && x.Email.ToLower() == trimmedEmail))
        {
            return Results.Conflict(new { message = "Email already belongs to another account." });
        }

        // Validate role
        var requestedRoleNames = request.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requestedRoleNames.Length != 1 ||
            !ActorAccountPolicy.IsAllowedGoogleActorRole(requestedRoleNames[0]))
        {
            return Results.BadRequest(new { message = "Each account must have exactly one Google-enabled actor role: ADMIN, CLUB_MANAGER, or CLUB_MEMBER." });
        }

        // Get role from DB
        var roles = await db.Roles
            .Where(x => requestedRoleNames.Contains(x.Name))
            .ToListAsync();

        if (roles.Count != requestedRoleNames.Length)
        {
            return Results.BadRequest(new { message = "One or more requested roles are invalid." });
        }

        var requestedRoleName = roles.Single().Name;
        var currentRoleName = user.UserRoles.SingleOrDefault()?.Role.Name;
        var targetIsSuperAdmin = currentRoleName == AuthRoles.Admin;

        // Check authorization for ADMIN operations
        if (!actor.IsInRole(AuthRoles.Admin) &&
            (targetIsSuperAdmin || requestedRoleName == AuthRoles.Admin))
        {
            return Results.Forbid();
        }

        // Prevent self-deactivation or self-role-change
        if (id == actor.GetUserId() &&
            (!request.IsActive ||
             !string.Equals(currentRoleName, requestedRoleName, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.BadRequest(new { message = "You cannot deactivate your own account or change your own actor role." });
        }

        // Prevent deactivating final ADMIN
        if (targetIsSuperAdmin &&
            (!request.IsActive || requestedRoleName != AuthRoles.Admin))
        {
            var otherActiveSuperAdmins = await db.Users.CountAsync(x =>
                x.Id != id &&
                x.IsActive &&
                !x.IsLocked &&
                x.UserRoles.Any(ur => ur.Role.Name == AuthRoles.Admin));

            if (otherActiveSuperAdmins == 0)
            {
                return Results.Conflict(new { message = "The final active ADMIN account cannot be deactivated or reassigned." });
            }
        }

        // Updating an allow-listed e-mail detaches any prior Google account.
        // The holder of the new e-mail must complete Google sign-in again.
        var emailChanged = !string.Equals(user.Email, trimmedEmail, StringComparison.OrdinalIgnoreCase);

        // Update user
        user.FullName = request.FullName.Trim();
        user.Email = trimmedEmail;
        user.IsActive = request.IsActive;
        if (emailChanged)
        {
            user.GoogleSubject = null;
        }

        db.UserRoles.RemoveRange(user.UserRoles);
        db.UserRoles.AddRange(roles.Select(role =>
            new UserRole { UserId = user.Id, RoleId = role.Id }));

        await db.SaveChangesAsync();

        var statusOrRoleChanged = !request.IsActive ||
            emailChanged ||
            !string.Equals(currentRoleName, requestedRoleName, StringComparison.OrdinalIgnoreCase);

        if (statusOrRoleChanged)
        {
            user.SecurityVersion++;
            cache.Remove($"sec_stamp:user:{user.Id}");
        }

        await db.SaveChangesAsync();

        // Revoke tokens if status changed
        if (statusOrRoleChanged)
        {
            await refreshTokenService.RevokeForUserAsync(user.Id);
        }

        logger.LogInformation("User updated: {UserId}, Email: {Email}, Roles: {Roles}, Active: {IsActive}, ActorId: {ActorId}",
            user.Id, user.Email, requestedRoleName, user.IsActive, actor.GetUserId());

        return Results.Ok(ToSummary(user, roles.Select(x => x.Name)));
    }

    private static async Task<IResult> HandleDisableUser(
        int id,
        AuthDbContext db,
        ClaimsPrincipal actor,
        RefreshTokenService refreshTokenService,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var user = await db.Users
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (id == actor.GetUserId())
        {
            return Results.BadRequest(new { message = "You cannot deactivate your own account." });
        }

        var targetIsAdmin = user.UserRoles.Any(x => x.Role.Name == AuthRoles.Admin);
        if (targetIsAdmin && !actor.IsInRole(AuthRoles.Admin))
        {
            return Results.Forbid();
        }

        if (!user.IsActive)
        {
            return Results.Ok(new { success = true });
        }

        if (targetIsAdmin && !user.IsLocked)
        {
            var otherActiveAdmins = await db.Users.CountAsync(x =>
                x.Id != id && x.IsActive && !x.IsLocked &&
                x.UserRoles.Any(ur => ur.Role.Name == AuthRoles.Admin), cancellationToken);
            if (otherActiveAdmins == 0)
            {
                return Results.Conflict(new { message = "The final active ADMIN account cannot be deactivated." });
            }
        }

        user.IsActive = false;
        user.SecurityVersion++;
        await db.SaveChangesAsync(cancellationToken);
        await refreshTokenService.RevokeForUserAsync(user.Id);
        await transaction.CommitAsync(cancellationToken);
        cache.Remove($"sec_stamp:user:{user.Id}");

        loggerFactory.CreateLogger("AuthService.UserManagement")
            .LogInformation("User deactivated: {UserId}, ActorId: {ActorId}", user.Id, actor.GetUserId());
        return Results.Ok(new { success = true });
    }

    private static Task<IResult> HandleAssignRole(
        int id,
        JsonElement request,
        AuthDbContext db,
        ClaimsPrincipal actor,
        RefreshTokenService refreshTokenService,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request.ValueKind != JsonValueKind.Object ||
            !request.TryGetProperty("roleId", out var roleIdValue) ||
            !TryReadRoleId(roleIdValue, out var roleId))
        {
            return Task.FromResult<IResult>(Results.BadRequest(new { message = "roleId must be a positive numeric role ID." }));
        }

        return ReplaceActorRoleAsync(id, roleId, null, db, actor, refreshTokenService,
            cache, loggerFactory, cancellationToken);
    }

    private static Task<IResult> HandleRemoveRole(
        int id,
        string roleId,
        AuthDbContext db,
        ClaimsPrincipal actor,
        RefreshTokenService refreshTokenService,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(roleId, out var removedRoleId) || removedRoleId <= 0)
        {
            return Task.FromResult<IResult>(Results.BadRequest(new { message = "roleId must be a positive numeric role ID." }));
        }

        return ReplaceActorRoleAsync(id, null, removedRoleId, db, actor, refreshTokenService,
            cache, loggerFactory, cancellationToken);
    }

    private static bool TryReadRoleId(JsonElement value, out int roleId)
    {
        roleId = 0;
        return (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out roleId) && roleId > 0) ||
               (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out roleId) && roleId > 0);
    }

    private static async Task<IResult> ReplaceActorRoleAsync(
        int id,
        int? assignedRoleId,
        int? removedRoleId,
        AuthDbContext db,
        ClaimsPrincipal actor,
        RefreshTokenService refreshTokenService,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.UserRoles.Count != 1)
        {
            return Results.Conflict(new { message = "Account must have exactly one actor role before a role change." });
        }
        var currentRole = user.UserRoles.Single();

        if (removedRoleId.HasValue && currentRole.RoleId != removedRoleId.Value)
        {
            return Results.NotFound();
        }

        var desiredRole = await db.Roles.FirstOrDefaultAsync(x => x.Id ==
            (assignedRoleId ?? 0), cancellationToken);
        if (removedRoleId.HasValue)
        {
            desiredRole = await db.Roles.FirstOrDefaultAsync(x => x.Name == AuthRoles.ClubMember,
                cancellationToken);
        }

        if (desiredRole is null)
        {
            return Results.NotFound();
        }

        if (!ActorAccountPolicy.IsAllowedGoogleActorRole(desiredRole.Name))
        {
            return Results.BadRequest(new { message = "Only ADMIN, CLUB_MANAGER, and CLUB_MEMBER actor roles can be assigned." });
        }

        if (!actor.IsInRole(AuthRoles.Admin) &&
            (currentRole.Role.Name == AuthRoles.Admin || desiredRole.Name == AuthRoles.Admin))
        {
            return Results.Forbid();
        }

        if (currentRole.RoleId == desiredRole.Id)
        {
            return Results.Ok(new { success = true });
        }

        if (id == actor.GetUserId())
        {
            return Results.BadRequest(new { message = "You cannot change your own actor role." });
        }

        if (currentRole.Role.Name == AuthRoles.Admin && user.IsActive && !user.IsLocked)
        {
            var otherActiveAdmins = await db.Users.CountAsync(x =>
                x.Id != id && x.IsActive && !x.IsLocked &&
                x.UserRoles.Any(ur => ur.Role.Name == AuthRoles.Admin), cancellationToken);
            if (otherActiveAdmins == 0)
            {
                return Results.Conflict(new { message = "The final active ADMIN account cannot be reassigned." });
            }
        }

        db.UserRoles.Remove(currentRole);
        await db.SaveChangesAsync(cancellationToken);
        db.UserRoles.Add(new UserRole { UserId = id, RoleId = desiredRole.Id });
        user.SecurityVersion++;
        await db.SaveChangesAsync(cancellationToken);
        await refreshTokenService.RevokeForUserAsync(id);
        await transaction.CommitAsync(cancellationToken);
        cache.Remove($"sec_stamp:user:{id}");

        loggerFactory.CreateLogger("AuthService.UserManagement")
            .LogInformation("Actor role changed for UserId: {UserId}, RoleId: {RoleId}, ActorId: {ActorId}",
                id, desiredRole.Id, actor.GetUserId());
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> HandleLockUser(
        int id,
        AuthDbContext db,
        ClaimsPrincipal actor,
        RefreshTokenService refreshTokenService,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AuthService.UserManagement");

        // Find user
        var user = await db.Users
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (user is null)
        {
            return Results.NotFound();
        }

        // Prevent self-lock
        if (id == actor.GetUserId())
        {
            return Results.BadRequest(new { message = "You cannot lock your own account." });
        }

        var targetIsSuperAdmin = user.UserRoles.Any(x => x.Role.Name == AuthRoles.Admin);
        if (targetIsSuperAdmin && !actor.IsInRole(AuthRoles.Admin))
        {
            return Results.Forbid();
        }

        // Prevent locking final ADMIN
        if (targetIsSuperAdmin && user.IsActive && !user.IsLocked)
        {
            var otherActiveSuperAdmins = await db.Users.CountAsync(x =>
                x.Id != id &&
                x.IsActive &&
                !x.IsLocked &&
                x.UserRoles.Any(ur => ur.Role.Name == AuthRoles.Admin));

            if (otherActiveSuperAdmins == 0)
            {
                return Results.Conflict(new { message = "The final active ADMIN account cannot be locked." });
            }
        }

        user.IsLocked = true;
        user.SecurityVersion++;
        await db.SaveChangesAsync();
        cache.Remove($"sec_stamp:user:{user.Id}");
        await refreshTokenService.RevokeForUserAsync(user.Id);

        logger.LogInformation("User locked: {UserId}, ActorId: {ActorId}", user.Id, actor.GetUserId());

        return Results.NoContent();
    }

    private static async Task<IResult> HandleUnlockUser(
        int id,
        AuthDbContext db,
        ClaimsPrincipal actor,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AuthService.UserManagement");

        // Find user
        var user = await db.Users
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (user is null)
        {
            return Results.NotFound();
        }

        // Prevent self-unlock
        if (id == actor.GetUserId())
        {
            return Results.BadRequest(new { message = "You cannot unlock your own account." });
        }

        // Check ADMIN authorization
        if (user.UserRoles.Any(x => x.Role.Name == AuthRoles.Admin) &&
            !actor.IsInRole(AuthRoles.Admin))
        {
            return Results.Forbid();
        }

        user.IsLocked = false;
        user.SecurityVersion++;
        await db.SaveChangesAsync();
        cache.Remove($"sec_stamp:user:{user.Id}");

        logger.LogInformation("User unlocked: {UserId}, ActorId: {ActorId}", user.Id, actor.GetUserId());

        return Results.NoContent();
    }

    public static UserSummary ToSummary(User user, IEnumerable<string>? withRoles = null)
    {
        var roles = withRoles?.ToArray() ??
            user.UserRoles.Select(x => x.Role.Name).OrderBy(x => x).ToArray();

        return new UserSummary(
            user.Id,
            user.Username,
            user.FullName,
            user.Email,
            roles,
            user.IsActive,
            user.IsLocked);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
