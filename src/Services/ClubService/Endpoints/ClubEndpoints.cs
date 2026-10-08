using System.Security.Claims;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Extensions;
using ClubService.Mappers;
using ClubService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClubService.Endpoints;

public static class ClubEndpoints
{
    public static void MapClubEndpoints(this WebApplication app)
    {
        var clubs = app.MapGroup("/api/clubs")
            .WithTags("Clubs")
            .RequireAuthorization(AuthPolicies.BusinessAccess);

        // GET /api/clubs - List all clubs
        clubs.MapGet("/", GetAllClubs)
            .WithName("GetAllClubs")
            .WithDescription("Get all clubs with optional filtering");

        // GET /api/clubs/categories - List all categories with counts
        clubs.MapGet("/categories", GetClubCategories)
            .WithName("GetClubCategories")
            .WithDescription("Get all club categories with counts");

        // POST /api/clubs/categories - Create new category (Admin only)
        clubs.MapPost("/categories", CreateClubCategory)
            .WithName("CreateClubCategory")
            .WithDescription("Create a new club category (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);

        // GET /api/clubs/me/managed - Get clubs managed by current user
        clubs.MapGet("/me/managed", GetManagedClubs)
            .WithName("GetManagedClubs")
            .WithDescription("Get clubs managed by current user");

        // GET /api/clubs/me/memberships - Get memberships of current user
        clubs.MapGet("/me/memberships", GetMemberships)
            .WithName("GetMyMemberships")
            .WithDescription("Get current user's club memberships");

        // GET /api/clubs/me/access - Get access summary for current user
        clubs.MapGet("/me/access", GetAccessSummary)
            .WithName("GetMyAccessSummary")
            .WithDescription("Get access summary for current user");

        // GET /api/clubs/{id} - Get club by ID
        clubs.MapGet("/{id:int}", GetClubById)
            .WithName("GetClubById")
            .WithDescription("Get club details by ID");

        // GET /api/clubs/manager/{managerUserId} - Get clubs for manager
        clubs.MapGet("/manager/{managerUserId:int}", GetClubsForManager)
            .WithName("GetClubsForManager")
            .WithDescription("Get clubs managed by a specific user");

        // POST /api/clubs - Create new club (Admin only)
        clubs.MapPost("/", CreateClub)
            .WithName("CreateClub")
            .WithDescription("Create a new club (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);

        // PUT /api/clubs/{id} - Update club (Admin only)
        clubs.MapPut("/{id:int}", UpdateClub)
            .WithName("UpdateClub")
            .WithDescription("Update club details (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);

        // DELETE /api/clubs/{id} - Soft delete club (Admin only)
        clubs.MapDelete("/{id:int}", DeleteClub)
            .WithName("DeleteClub")
            .WithDescription("Soft delete a club (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);

        // DELETE /api/clubs/{clubId}/direct - Direct delete club (Admin only)
        clubs.MapDelete("/{clubId:int}/direct", DirectDeleteClub)
            .WithName("DirectDeleteClub")
            .WithDescription("Direct delete a club without workflow (Admin only)")
            .RequireAuthorization(AuthPolicies.StudentAffairsAdministration);
    }

    private static async Task<IResult> GetAllClubs(
        string? search,
        string? category,
        string? campus,
        bool? active,
        bool? recruiting,
        ClubDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var query = db.Clubs
            .AsNoTracking()
            .Include(x => x.ManagerAssignments)
            .Include(x => x.Memberships)
            .AsSplitQuery()
            .AsQueryable();

        if (!user.IsStudentAffairsAdministrator())
        {
            var currentUserId = user.GetUserId();
            query = query.Where(x =>
                x.IsActive
                || x.ManagerAssignments.Any(m => m.ManagerUserId == currentUserId && m.IsActive));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x => x.Code.Contains(search) || x.Name.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(campus))
        {
            var normalizedCampus = CampusCodes.Normalize(campus);
            if (normalizedCampus != CampusCodes.Global)
            {
                query = query.Where(x => x.CampusCode == normalizedCampus);
            }
        }

        if (active.HasValue)
        {
            query = query.Where(x => x.IsActive == active);
        }

        if (recruiting.HasValue)
        {
            query = query.Where(x => x.IsRecruiting == recruiting);
        }

        var result = await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);

        return Results.Ok(result.Select(ClubMappers.ToDirectoryResponse));
    }

    private static async Task<IResult> GetManagedClubs(
        ClaimsPrincipal user,
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var clubs = await db.Clubs
            .AsNoTracking()
            .Include(x => x.ManagerAssignments)
            .Include(x => x.Memberships)
            .AsSplitQuery()
            .Where(x => x.ManagerAssignments.Any(m => m.ManagerUserId == userId && m.IsActive))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Results.Ok(clubs.Select(ClubMappers.ToResponse));
    }

    private static async Task<IResult> GetMemberships(
        ClaimsPrincipal user,
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var memberships = await db.ClubMemberships
            .AsNoTracking()
            .Include(x => x.Club)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(memberships.Select(ClubMappers.ToMembershipResponse));
    }

    private static async Task<IResult> GetAccessSummary(
        ClaimsPrincipal user,
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();

        var managedClubs = await db.ClubManagerAssignments
            .AsNoTracking()
            .Where(x => x.ManagerUserId == userId && x.IsActive)
            .Select(x => new { x.ClubId, x.Club.Name })
            .ToListAsync(cancellationToken);

        var memberships = await db.ClubMemberships
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Status == ClubMembershipStatuses.Approved)
            .Select(x => new { x.ClubId, x.Club.Name, x.Role })
            .ToListAsync(cancellationToken);

        var clubIds = managedClubs.Select(x => x.ClubId)
            .Concat(memberships.Select(x => x.ClubId))
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var activeManagers = await db.ClubManagerAssignments
            .AsNoTracking()
            .Where(x => clubIds.Contains(x.ClubId) && x.IsActive)
            .Select(x => new { x.ClubId, x.ManagerUserId })
            .ToListAsync(cancellationToken);

        var approvedMembers = await db.ClubMemberships
            .AsNoTracking()
            .Where(x => clubIds.Contains(x.ClubId) && x.Status == ClubMembershipStatuses.Approved)
            .Select(x => new { x.ClubId, x.UserId, x.Role })
            .ToListAsync(cancellationToken);

        var access = clubIds.Select(clubId =>
        {
            var managed = managedClubs.FirstOrDefault(x => x.ClubId == clubId);
            var membership = memberships.FirstOrDefault(x => x.ClubId == clubId);
            return new ClubAccessResponse(
                clubId,
                managed?.Name ?? membership?.Name ?? string.Empty,
                managed is not null,
                string.Equals(membership?.Role, ClubMemberRoles.Treasurer, StringComparison.OrdinalIgnoreCase),
                membership is not null,
                activeManagers.Where(x => x.ClubId == clubId).Select(x => x.ManagerUserId).Distinct().ToArray(),
                approvedMembers.Where(x => x.ClubId == clubId).Select(x => x.UserId).Distinct().ToArray(),
                approvedMembers
                    .Where(x => x.ClubId == clubId && x.Role == ClubMemberRoles.Treasurer)
                    .Select(x => x.UserId)
                    .Distinct()
                    .ToArray());
        });

        return Results.Ok(access);
    }

    private static async Task<IResult> GetClubById(
        int id,
        ClubDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var club = await db.Clubs
            .AsNoTracking()
            .Include(x => x.ManagerAssignments)
            .Include(x => x.Memberships)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (club is null)
        {
            return Results.NotFound();
        }

        var isAssignedManager = club.ManagerAssignments.Any(x => x.ManagerUserId == user.GetUserId() && x.IsActive);
        if (!club.IsActive && !user.IsStudentAffairsAdministrator() && !isAssignedManager)
        {
            return Results.NotFound();
        }

        var canViewPrivateDetails = user.IsStudentAffairsAdministrator() || isAssignedManager;
        return Results.Ok(canViewPrivateDetails
            ? (object)ClubMappers.ToResponse(club)
            : (object)ClubMappers.ToPublicDetailResponse(club));
    }

    private static async Task<IResult> GetClubsForManager(
        int managerUserId,
        ClubDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!user.IsStudentAffairsAdministrator() && managerUserId != user.GetUserId())
        {
            return Results.Forbid();
        }

        var clubs = await db.Clubs
            .AsNoTracking()
            .Include(x => x.ManagerAssignments)
            .Include(x => x.Memberships)
            .AsSplitQuery()
            .Where(x => x.ManagerAssignments.Any(m => m.ManagerUserId == managerUserId && m.IsActive))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Results.Ok(clubs.Select(ClubMappers.ToResponse));
    }

    private static async Task<IResult> CreateClub(
        CreateClubRequest request,
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { message = "Club code and name are required." });
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Clubs.AnyAsync(x => x.Code == code, cancellationToken))
        {
            return Results.Conflict(new { message = "Club code already exists." });
        }

        var campusCode = CampusCodes.Normalize(request.CampusCode);
        var normalizedCat = ValidationExtensions.NormalizeClubCategory(request.Category);
        if (normalizedCat == ClubCategories.Other && !string.IsNullOrWhiteSpace(request.Category))
        {
            var matchedCustom = await db.ClubCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Code == request.Category.Trim() || x.Name == request.Category.Trim(),
                    cancellationToken);
            if (matchedCustom is not null)
            {
                normalizedCat = matchedCustom.Code;
            }
        }

        var club = new Club
        {
            Code = code,
            Name = request.Name.Trim(),
            Category = normalizedCat,
            CampusCode = campusCode,
            Description = request.Description?.Trim() ?? string.Empty,
            LogoUrl = request.LogoUrl?.Trim(),
            ContactEmail = request.ContactEmail?.Trim() ?? string.Empty,
            ContactPhone = request.ContactPhone?.Trim() ?? string.Empty,
            ScheduleLabel = request.ScheduleLabel?.Trim(),
            IsRecruiting = request.IsRecruiting
        };

        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational())
        {
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        }

        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        db.AddOutboxMessage(new ClubCreatedEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            club.Id,
            club.Code,
            club.Name), EventRoutingKeys.ClubCreated);

        await db.SaveChangesAsync(cancellationToken);

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Results.Created($"/api/clubs/{club.Id}", ClubMappers.ToResponse(club));
    }

    private static async Task<IResult> UpdateClub(
        int id,
        UpdateClubRequest request,
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        var club = await db.Clubs
            .Include(x => x.ManagerAssignments)
            .Include(x => x.Memberships)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (club is null)
        {
            return Results.NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            club.Name = request.Name.Trim();
        }

        if (request.Category is not null)
        {
            var normalizedCat = ValidationExtensions.NormalizeClubCategory(request.Category);
            if (normalizedCat == ClubCategories.Other && !string.IsNullOrWhiteSpace(request.Category))
            {
                var matchedCustom = await db.ClubCategories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.Code == request.Category.Trim() || x.Name == request.Category.Trim(),
                        cancellationToken);
                if (matchedCustom is not null)
                {
                    normalizedCat = matchedCustom.Code;
                }
            }
            club.Category = normalizedCat;
        }

        if (request.Description is not null)
        {
            club.Description = request.Description.Trim();
        }

        if (request.LogoUrl is not null)
        {
            club.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
        }

        if (request.ContactEmail is not null)
        {
            club.ContactEmail = request.ContactEmail.Trim();
        }

        if (request.ContactPhone is not null)
        {
            club.ContactPhone = request.ContactPhone.Trim();
        }

        club.IsActive = request.IsActive;

        if (!string.IsNullOrWhiteSpace(request.CampusCode))
        {
            club.CampusCode = CampusCodes.Normalize(request.CampusCode);
        }

        if (request.ScheduleLabel is not null)
        {
            club.ScheduleLabel = string.IsNullOrWhiteSpace(request.ScheduleLabel) ? null : request.ScheduleLabel.Trim();
        }

        if (request.IsRecruiting.HasValue)
        {
            club.IsRecruiting = request.IsRecruiting.Value;
        }

        if (request.IsActive)
        {
            club.DeletedAtUtc = null;
            club.DeletedByUserId = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ClubMappers.ToResponse(club));
    }

    private static async Task<IResult> DeleteClub(
        int id,
        ClubDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var club = await db.Clubs
            .Include(x => x.ManagerAssignments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (club is null)
        {
            return Results.NotFound();
        }

        if (!club.IsActive)
        {
            return Results.NoContent();
        }

        club.IsActive = false;
        club.DeletedAtUtc = DateTimeOffset.UtcNow;
        club.DeletedByUserId = user.GetUserId();

        foreach (var assignment in club.ManagerAssignments.Where(x => x.IsActive))
        {
            assignment.IsActive = false;
            assignment.EndedAtUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DirectDeleteClub(
        int clubId,
        ClubDbContext db,
        HttpContext http,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (!http.User.IsStudentAffairsAdministrator())
            return Results.Forbid();

        var reviewerUserId = http.User.GetUserId();

        var club = await db.Clubs.FindAsync(new object[] { clubId }, ct);
        if (club is null)
            return Results.NotFound(new { message = "Club not found" });

        if (!club.IsActive)
            return Results.BadRequest(new { message = "Club is already deleted" });

        club.IsActive = false;
        club.DeletedAtUtc = DateTimeOffset.UtcNow;
        club.DeletedByUserId = reviewerUserId;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Club {ClubId} ({ClubName}) directly deleted by admin {AdminId}",
            clubId, club.Name, reviewerUserId);

        return Results.Ok(new { message = "Club deleted successfully", clubId, clubName = club.Name });
    }

    private static async Task<IResult> GetClubCategories(
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        var categories = await db.ClubCategories
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (categories.Count == 0)
        {
            var defaultCategories = new List<ClubCategory>
            {
                new() { Code = "TECHNOLOGY", Name = "Công nghệ", Description = "Câu lạc bộ công nghệ, kỹ thuật và phần mềm" },
                new() { Code = "ARTS", Name = "Nghệ thuật", Description = "Câu lạc bộ nghệ thuật, âm nhạc, biểu diễn và thiết kế" },
                new() { Code = "SPORTS", Name = "Thể thao", Description = "Câu lạc bộ thể thao, rèn luyện thể chất và võ thuật" },
                new() { Code = "VOLUNTEER", Name = "Tình nguyện", Description = "Câu lạc bộ tình nguyện, công tác xã hội và cộng đồng" },
                new() { Code = "ACADEMIC", Name = "Học thuật", Description = "Câu lạc bộ học thuật, ngoại ngữ và kỹ năng" },
                new() { Code = "BUSINESS", Name = "Kinh doanh", Description = "Câu lạc bộ kinh doanh, tài chính và khởi nghiệp" },
                new() { Code = "OTHER", Name = "Khác", Description = "Các câu lạc bộ sở thích và hoạt động chung khác" }
            };
            db.ClubCategories.AddRange(defaultCategories);
            await db.SaveChangesAsync(cancellationToken);
            categories = defaultCategories;
        }

        var activeClubs = await db.Clubs
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.Category)
            .ToListAsync(cancellationToken);

        var result = categories.Select(cat =>
        {
            var count = activeClubs.Count(c =>
                string.Equals(c, cat.Code, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, cat.Name, StringComparison.OrdinalIgnoreCase));

            return new ClubCategoryResponse(
                cat.Id,
                cat.Code,
                cat.Name,
                cat.Description,
                count,
                cat.CreatedAtUtc);
        }).ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateClubCategory(
        CreateClubCategoryRequest request,
        ClubDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { message = "Category name is required." });
        }

        var name = request.Name.Trim();
        var code = string.IsNullOrWhiteSpace(request.Code)
            ? ValidationExtensions.NormalizeClubCategory(name)
            : request.Code.Trim().ToUpperInvariant();

        if (code == ClubCategories.Other && !string.Equals(name, "Khác", StringComparison.OrdinalIgnoreCase))
        {
            code = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code))
            {
                code = "CAT_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            }
        }

        var existing = await db.ClubCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Name.ToLower() == name.ToLower() || x.Code.ToUpper() == code.ToUpper(),
                cancellationToken);

        if (existing is not null)
        {
            return Results.Conflict(new { message = $"Category '{name}' already exists." });
        }

        var category = new ClubCategory
        {
            Code = code,
            Name = name,
            Description = request.Description?.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.ClubCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/clubs/categories/{category.Id}", new ClubCategoryResponse(
            category.Id,
            category.Code,
            category.Name,
            category.Description,
            0,
            category.CreatedAtUtc));
    }
}
