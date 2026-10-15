using AdminService.Auditing;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Errors;
using AdminService.Security;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Data;
using AdminService.Services;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Endpoints;

public static class QuestEndpoints
{
    private static readonly (string Title, string Description, string Category, string Kind, string Scope, int RewardXp, int Target, string Status, string Icon)[] DefaultQuests =
    [
        (
            "Chiến dịch Xanh: Một giờ vì Trái Đất",
            "Tham gia chuỗi hoạt động thu gom rác thải, trồng cây xanh và lan tỏa lối sống bền vững trong khuôn viên trường.",
            "Community",
            "Chiến dịch",
            "Toàn bộ sinh viên",
            40,
            200,
            "published",
            "leaf"
        ),
        (
            "Workshop AI & Khởi nghiệp Đổi mới sáng tạo",
            "Tham dự chuỗi hội thảo chuyên sâu về Trí tuệ nhân tạo thế hệ mới và xây dựng đề án khởi nghiệp công nghệ.",
            "Entrepreneurship",
            "Cá nhân",
            "Toàn bộ sinh viên",
            50,
            120,
            "published",
            "sparkles"
        ),
        (
            "Thử thách Đọc sách & Nghiên cứu Khoa học",
            "Đọc và tóm tắt ít nhất 3 bài báo khoa học hoặc sách chuyên ngành, chia sẻ bài cảm nhận với cộng đồng.",
            "Research",
            "Cá nhân",
            "Tân sinh viên",
            30,
            80,
            "published",
            "target"
        ),
        (
            "Giải đấu Thể thao & Rèn luyện Thể chất",
            "Đăng ký thi đấu giao hữu các môn thể thao tập thể nhằm nâng cao thể lực và thắt chặt tinh thần đồng đội.",
            "CultureSports",
            "Theo nhóm",
            "Toàn bộ sinh viên",
            45,
            150,
            "draft",
            "sparkles"
        )
    ];

    public static IEndpointRouteBuilder MapQuestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapGroup(endpoints, "/api/v1/quests");
        MapGroup(endpoints, "/api/quests");

        return endpoints;
    }

    private static void MapGroup(IEndpointRouteBuilder endpoints, string prefix)
    {
        var group = endpoints.MapGroup(prefix)
            .WithTags("Quests and Campaigns");

        group.MapGet("/", GetQuestsAsync)
            .WithName($"GetQuests_{prefix.Replace("/", "_")}")
            .AllowAnonymous()
            .Produces<IReadOnlyList<QuestResponse>>();

        group.MapGet("/me", GetMyQuestsAsync)
            .WithName($"GetMyQuests_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.AnyActor)
            .Produces<IReadOnlyList<QuestParticipantResponse>>();

        group.MapGet("/{id}", GetQuestByIdAsync)
            .WithName($"GetQuestById_{prefix.Replace("/", "_")}")
            .AllowAnonymous()
            .Produces<QuestResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateQuestAsync)
            .WithName($"CreateQuest_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<QuestResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/{id}", UpdateQuestAsync)
            .WithName($"UpdateQuest_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<QuestResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/{id}/status", UpdateQuestStatusAsync)
            .WithName($"UpdateQuestStatus_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces<QuestResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id}", DeleteQuestAsync)
            .WithName($"DeleteQuest_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.BackofficeUser)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/join", JoinQuestAsync)
            .WithName($"JoinQuest_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.AnyActor)
            .Produces<QuestParticipantResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/complete", CompleteQuestAsync)
            .WithName($"CompleteQuest_{prefix.Replace("/", "_")}")
            .RequireAuthorization(AdminPolicies.AnyActor)
            .Produces<QuestParticipantResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetQuestsAsync(string? semester, string? period, string? season,
        string? status, string? category, string? search, string? campusCode, int? page, int? pageSize, bool? recommended,
        AdminDbContext dbContext, HttpContext http, Microsoft.AspNetCore.Authorization.IAuthorizationService authorization,
        IConfiguration configuration, StudentExperienceSourceClient sources, CancellationToken cancellationToken)
    {
        if (page is < 1 || pageSize is < 1 or > 100 || search?.Length > 200)
            return ClubReportHub.Shared.Data.ApiOperations.Error(400, "INVALID_QUEST_QUERY", "Check filters and pagination.");
        var manage = (await authorization.AuthorizeAsync(http.User, null, AdminPolicies.BackofficeUser)).Succeeded;
        if (recommended ?? (http.User.Identity?.IsAuthenticated == true && !manage))
            return await StudentQuestRecommendations.ReadAsync(dbContext, http, configuration, sources, semester ?? period ?? season,
                status, category, search, campusCode, page, pageSize, cancellationToken);
        var query = dbContext.Quests.AsNoTracking().AsQueryable();
        if (!manage) query = query.Where(x => x.Status == "published" && x.CriteriaSnapshotJson != null);
        var targetSemester = semester ?? period ?? season;
        if (!string.IsNullOrWhiteSpace(targetSemester)) query = query.Where(x => x.SemesterCode == targetSemester);
        if (!string.IsNullOrWhiteSpace(status) && status != "all") query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(category) && category != "all") query = query.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(campusCode) && campusCode != CampusCodes.Global)
            query = query.Where(x => x.CampusCode == CampusCodes.Global || x.CampusCode == campusCode);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Title.Contains(search) || x.Description.Contains(search));
        var current = page ?? 1; var size = pageSize ?? 20;
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size)
            .Select(x => new
            {
                Quest = x,
                Joined = dbContext.QuestParticipants.Count(p => p.QuestId == x.Id),
                Completed = dbContext.QuestParticipants.Count(p => p.QuestId == x.Id && p.Status == "Completed")
            }).ToListAsync(cancellationToken);
        var items = rows.Select(x => QuestResponse.From(x.Quest, x.Joined, x.Completed) with
        { CreatedByName = manage ? x.Quest.CreatedByName : null }).ToArray();
        http.Response.Headers["X-Total-Count"] = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return page.HasValue || pageSize.HasValue ? Results.Ok(new
        {
            items,
            page = current,
            pageSize = size,
            totalItems = total,
            totalPages = (int)Math.Ceiling((decimal)total / size)
        }) : Results.Ok(items);
    }

    private static async Task<IResult> GetQuestByIdAsync(string id, AdminDbContext dbContext, HttpContext http,
        Microsoft.AspNetCore.Authorization.IAuthorizationService authorization, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId)) return Results.NotFound();
        var quest = await dbContext.Quests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);
        if (quest is null) return Results.NotFound();
        var manage = (await authorization.AuthorizeAsync(http.User, null, AdminPolicies.BackofficeUser)).Succeeded;
        var own = http.User.Identity?.IsAuthenticated == true && await dbContext.QuestParticipants.AsNoTracking()
            .AnyAsync(x => x.QuestId == parsedId && x.StudentUserId == http.User.GetUserId(), cancellationToken);
        if (!manage && !own && (quest.Status != "published" || quest.CriteriaSnapshotJson is null)) return Results.NotFound();
        var joined = await dbContext.QuestParticipants.CountAsync(x => x.QuestId == parsedId, cancellationToken);
        var completed = await dbContext.QuestParticipants.CountAsync(x => x.QuestId == parsedId && x.Status == "Completed", cancellationToken);
        return Results.Ok(QuestResponse.From(quest, joined, completed) with { CreatedByName = manage ? quest.CreatedByName : null });
    }

    private static async Task<IResult> CreateQuestAsync(
        CreateQuestRequest request,
        AdminDbContext dbContext,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        var title = (request.Title ?? request.Name)?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return Results.BadRequest(new { message = "Tên nhiệm vụ không được để trống." });
        }

        var description = request.Description?.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            return Results.BadRequest(new { message = "Mô tả và điều kiện hoàn thành không được để trống." });
        }

        DateTimeOffset? deadline = null;
        var deadlineInput = request.Deadline ?? request.DueDate;
        if (!string.IsNullOrWhiteSpace(deadlineInput) && DateTimeOffset.TryParse(deadlineInput, out var parsedDeadline))
        {
            deadline = parsedDeadline;
        }

        var quest = new Quest
        {
            Title = title,
            Description = description,
            Category = request.Category?.Trim() ?? request.Tag?.Trim() ?? "Community",
            Kind = request.Kind?.Trim() ?? request.ReportType?.Trim() ?? "Cá nhân",
            Scope = request.Scope?.Trim() ?? "Toàn bộ sinh viên",
            RewardXp = request.RewardXp ?? request.Reward ?? 30,
            TargetParticipants = request.TargetParticipants ?? request.Target ?? 100,
            SemesterCode = request.SemesterCode?.Trim() ?? request.Period?.Trim() ?? "FALL2026",
            CampusCode = request.CampusCode?.Trim() ?? currentActor.CampusCode ?? "GLOBAL",
            Icon = request.Icon?.Trim() ?? "sparkles",
            DeadlineUtc = deadline,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "published" : request.Status.Trim().ToLowerInvariant(),
            CreatedByUserId = currentActor.UserId,
            CreatedByName = currentActor.Email ?? currentActor.SubjectId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        if (quest.Status is not ("draft" or "published" or "paused" or "completed") || title.Length > 200 || description.Length > 2000
            || quest.TargetParticipants is < 1 or > 100000 || !string.IsNullOrWhiteSpace(deadlineInput) && deadline is null)
            return ApiOperations.Error(400, "INVALID_QUEST", "Check content, status, deadline and target.");
        if (request.Criteria is not null)
        {
            var error = await StudentQuestService.ValidateAsync(request.Criteria, quest.SemesterCode, "PROJECT", dbContext, cancellationToken);
            if (error is not null) return ApiOperations.Error(error == "SCORE_POLICY_UNAPPROVED" ? 503 : 400, error, "Check the approved quest criteria.");
            quest.CriteriaVersion = Guid.NewGuid(); quest.CriteriaSnapshotJson = VerifiedExperienceService.Json(request.Criteria);
        }
        if (quest.Status == "published" && quest.CriteriaSnapshotJson is null)
            return ApiOperations.Error(400, "QUEST_CRITERIA_REQUIRED", "Publishing requires verified contribution criteria.");

        dbContext.Quests.Add(quest);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Json(QuestResponse.From(quest, 0, 0), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateQuestAsync(
        string id,
        UpdateQuestRequest request,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId))
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var quest = await dbContext.Quests
            .Include(x => x.Participants)
            .SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);

        if (quest is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        if (request.ExpectedVersion.HasValue && request.ExpectedVersion != quest.Version)
            return ApiOperations.Error(409, "STALE_VERSION", "Reload the quest.");
        if (quest.Participants.Count > 0 && (request.Criteria is not null || request.SemesterCode is not null || request.Period is not null
            || request.CampusCode is not null || request.Deadline is not null || request.DueDate is not null))
            return ApiOperations.Error(409, "QUEST_CRITERIA_IMMUTABLE", "Create a new quest version; joined criteria are frozen.");
        if (request.Criteria is not null)
        {
            if (!request.ExpectedVersion.HasValue) return ApiOperations.Error(400, "EXPECTED_VERSION_REQUIRED", "Provide the quest version.");
            var error = await StudentQuestService.ValidateAsync(request.Criteria, request.SemesterCode ?? request.Period ?? quest.SemesterCode,
                "PROJECT", dbContext, cancellationToken);
            if (error is not null) return ApiOperations.Error(error == "SCORE_POLICY_UNAPPROVED" ? 503 : 400, error, "Check the approved quest criteria.");
            quest.CriteriaVersion = Guid.NewGuid(); quest.CriteriaSnapshotJson = VerifiedExperienceService.Json(request.Criteria);
        }

        var title = (request.Title ?? request.Name)?.Trim();
        if (!string.IsNullOrWhiteSpace(title))
        {
            quest.Title = title;
        }

        if (request.Description is not null)
        {
            quest.Description = request.Description.Trim();
        }

        var category = request.Category ?? request.Tag;
        if (!string.IsNullOrWhiteSpace(category))
        {
            quest.Category = category.Trim();
        }

        var kind = request.Kind ?? request.ReportType;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            quest.Kind = kind.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            quest.Scope = request.Scope.Trim();
        }

        if (request.RewardXp.HasValue || request.Reward.HasValue)
        {
            quest.RewardXp = request.RewardXp ?? request.Reward!.Value;
        }

        if (request.TargetParticipants.HasValue || request.Target.HasValue)
        {
            quest.TargetParticipants = request.TargetParticipants ?? request.Target!.Value;
        }

        var semester = request.SemesterCode ?? request.Period;
        if (!string.IsNullOrWhiteSpace(semester))
        {
            quest.SemesterCode = semester.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.CampusCode))
        {
            quest.CampusCode = request.CampusCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Icon))
        {
            quest.Icon = request.Icon.Trim();
        }

        var deadlineInput = request.Deadline ?? request.DueDate;
        if (!string.IsNullOrWhiteSpace(deadlineInput) && DateTimeOffset.TryParse(deadlineInput, out var parsedDeadline))
        {
            quest.DeadlineUtc = parsedDeadline;
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            quest.Status = request.Status.Trim().ToLowerInvariant();
        }

        if (quest.Status == "published" && quest.CriteriaSnapshotJson is null)
            return ApiOperations.Error(400, "QUEST_CRITERIA_REQUIRED", "Publishing requires verified contribution criteria.");
        quest.Version = Guid.NewGuid();
        quest.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var joined = quest.Participants.Count;
        var completed = quest.Participants.Count(p => p.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
        return Results.Ok(QuestResponse.From(quest, joined, completed));
    }

    private static async Task<IResult> UpdateQuestStatusAsync(
        string id,
        UpdateQuestStatusRequest request,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId))
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var quest = await dbContext.Quests
            .Include(x => x.Participants)
            .SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);

        if (quest is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var validStatuses = new[] { "draft", "published", "paused", "completed" };
        var next = request.Status?.Trim().ToLowerInvariant() ?? "";
        if (!validStatuses.Contains(next))
        {
            return Results.BadRequest(new { message = "Trạng thái không hợp lệ. Cho phép: draft, published, paused, completed." });
        }

        quest.Status = next;
        if (next == "published" && quest.CriteriaSnapshotJson is null)
            return ApiOperations.Error(400, "QUEST_CRITERIA_REQUIRED", "Publishing requires verified contribution criteria.");
        quest.Version = Guid.NewGuid();
        quest.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var joined = quest.Participants.Count;
        var completed = quest.Participants.Count(p => p.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
        return Results.Ok(QuestResponse.From(quest, joined, completed));
    }

    private static async Task<IResult> DeleteQuestAsync(
        string id,
        AdminDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId))
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        var quest = await dbContext.Quests
            .Include(x => x.Participants)
            .SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);

        if (quest is null)
        {
            return Results.NotFound(new { message = "Không tìm thấy nhiệm vụ với ID được cung cấp." });
        }

        // Only permit deleting if not completed by any participants
        if (quest.Participants.Any(p => p.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)))
        {
            return Results.BadRequest(new { message = "Không thể xóa nhiệm vụ đã có sinh viên hoàn thành ghi nhận điểm." });
        }

        dbContext.Quests.Remove(quest);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> JoinQuestAsync(string id, JoinQuestRequest? request, AdminDbContext dbContext,
        ICurrentActor currentActor, HttpContext http, CurrentUserProfileClient profiles,
        StudentExperienceSourceClient sources, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId)) return Results.NotFound();
        var studentUserId = currentActor.UserId ?? 0;
        if (studentUserId <= 0) return Results.Forbid();
        if (request?.Note?.Length > 500) return Results.BadRequest();
        InternalUserProfile? profile;
        try { profile = (await profiles.GetAsync([studentUserId], cancellationToken)).GetValueOrDefault(studentUserId); }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        { return ClubReportHub.Shared.Data.ApiOperations.Error(503, "PROFILE_SERVICE_UNAVAILABLE", "The profile authority is unavailable."); }
        if (profile is null || !profile.IsActive || profile.IsLocked) return Results.Forbid();
        var quest = await dbContext.Quests.SingleOrDefaultAsync(x => x.Id == parsedId, cancellationToken);
        if (quest is null) return Results.NotFound();
        if (quest.CampusCode != CampusCodes.Global && quest.CampusCode != profile.CampusCode) return Results.Forbid();
        if (quest.Status != "published" || quest.DeadlineUtc < DateTimeOffset.UtcNow)
            return ClubReportHub.Shared.Data.ApiOperations.Error(409, "QUEST_NOT_OPEN", "The quest is not published or its deadline passed.");
        if (quest.CriteriaSnapshotJson is null || !quest.CriteriaVersion.HasValue)
            return ClubReportHub.Shared.Data.ApiOperations.Error(409, "QUEST_CRITERIA_UNCONFIGURED", "Configure verified contribution criteria before joining.");
        var validatedVersion = quest.Version;
        var criteria = AdminService.Services.VerifiedExperienceService.Read<QuestCriteria>(quest.CriteriaSnapshotJson);
        var criteriaError = await StudentQuestService.ValidateAsync(criteria, quest.SemesterCode,
            http.RequestServices.GetRequiredService<IConfiguration>()["Experience:Environment"] ?? "PROJECT", dbContext, cancellationToken);
        if (criteriaError is not null) return ApiOperations.Error(409, criteriaError, "The quest criteria need an approved matching semester policy.");
        try
        {
            var available = await sources.CandidatesAsync(new(studentUserId, 1, 100, quest.SemesterCode, criteria.EligibleActivityIds,
                IncludeRegistered: true, SessionIds: criteria.EligibleSessionIds), cancellationToken);
            if (!available.Items.Any(x => x.IsEligible && x.PillarWeights.Any(w => w.Pillar == criteria.TargetPillar && w.Weight > 0)))
                return ClubReportHub.Shared.Data.ApiOperations.Error(409, "QUEST_SOURCE_UNAVAILABLE", "No eligible upcoming activity matches this quest.");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        { return ClubReportHub.Shared.Data.ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Quest source eligibility is unavailable."); }
        return await ClubReportHub.Shared.Data.ApiOperations.ExecuteAsync(http, dbContext, request ?? new JoinQuestRequest(null), async () =>
        {
            await dbContext.Entry(quest).ReloadAsync(cancellationToken);
            if (quest.Version != validatedVersion)
                return ApiOperations.Error(409, "STALE_QUEST", "The quest changed during eligibility validation; reload it.");
            if (quest.Status != "published" || quest.DeadlineUtc < DateTimeOffset.UtcNow)
                return ClubReportHub.Shared.Data.ApiOperations.Error(409, "QUEST_NOT_OPEN", "Reload the quest.");
            var existing = await dbContext.QuestParticipants.SingleOrDefaultAsync(x => x.QuestId == parsedId && x.StudentUserId == studentUserId, cancellationToken);
            if (existing is not null) return Results.Ok(await AdminService.Services.StudentQuestService.ResponseAsync(existing, dbContext, sources, cancellationToken));
            var participant = new QuestParticipant
            {
                QuestId = parsedId,
                StudentUserId = studentUserId,
                StudentName = profile.DisplayName,
                StudentEmail = profile.Email,
                CampusCode = profile.CampusCode,
                Note = request?.Note?.Trim(),
                CriteriaVersion = quest.CriteriaVersion,
                CriteriaSnapshotJson = AdminService.Services.VerifiedExperienceService.Json(new JoinedQuestCriteria(criteria,
                    quest.SemesterCode, quest.CampusCode, quest.DeadlineUtc, quest.CriteriaVersion.Value))
            };
            dbContext.QuestParticipants.Add(participant);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Json(await AdminService.Services.StudentQuestService.ResponseAsync(participant, dbContext, sources, cancellationToken), statusCode: 201);
        }, cancellationToken, required: false);
    }

    private static async Task<IResult> CompleteQuestAsync(string id, CompleteQuestRequest? request, AdminDbContext dbContext,
        ICurrentActor currentActor, HttpContext http, StudentExperienceSourceClient sources, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId)) return Results.NotFound();
        var targetUserId = currentActor.UserId ?? 0;
        if (targetUserId <= 0 || request?.StudentUserId is { } requestedUser && requestedUser != targetUserId) return Results.Forbid();
        if (request?.Note?.Length > 500 || request?.LinkedContributionIds is { Count: > 100 }
            || request?.LinkedContributionIds?.Distinct().Count() != request?.LinkedContributionIds?.Count) return Results.BadRequest();
        var participant = await dbContext.QuestParticipants.SingleOrDefaultAsync(x => x.QuestId == parsedId
            && x.StudentUserId == targetUserId, cancellationToken);
        if (participant is null) return ClubReportHub.Shared.Data.ApiOperations.Error(409, "QUEST_NOT_JOINED", "Join the quest first.");
        var snapshot = AdminService.Services.StudentQuestService.Snapshot(participant);
        if (snapshot is null) return ClubReportHub.Shared.Data.ApiOperations.Error(409, "QUEST_CRITERIA_UNCONFIGURED", "Legacy participation has no trusted criteria snapshot.");
        // Re-check source proofs before idempotency replay, too. A cached success cannot restore revoked work.
        var currentProof = await StudentQuestService.ProofAsync(participant, dbContext, sources, cancellationToken, request?.LinkedContributionIds);
        if (!currentProof.SourcesAvailable) return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Current quest source validity is unavailable.");
        if (request?.LinkedContributionIds?.Any(x => !currentProof.Ids.Contains(x)) == true)
            return ApiOperations.Error(400, "INVALID_QUEST_PROOF", "Every proof must be an eligible verified contribution of the author.");
        if (currentProof.Ids.Count < snapshot.Criteria.MinimumVerifiedContributions || currentProof.Points < snapshot.Criteria.MinimumAllocatedPoints)
            return ApiOperations.Error(409, "QUEST_PROOF_REQUIRED", "Current verified contributions do not meet the criteria.");
        return await ClubReportHub.Shared.Data.ApiOperations.ExecuteAsync(http, dbContext, request ?? new CompleteQuestRequest(null, null), async () =>
        {
            await dbContext.Entry(participant).ReloadAsync(cancellationToken);
            if (!request?.ExpectedVersion.HasValue ?? true)
                return ClubReportHub.Shared.Data.ApiOperations.Error(400, "EXPECTED_VERSION_REQUIRED", "Provide the participation version.");
            if (participant.Version != request!.ExpectedVersion)
                return ClubReportHub.Shared.Data.ApiOperations.Error(409, "STALE_VERSION", "Reload your participation.");
            var proof = await AdminService.Services.StudentQuestService.ProofAsync(participant, dbContext, sources, cancellationToken, request.LinkedContributionIds);
            if (!proof.SourcesAvailable) return ApiOperations.Error(503, "ACTIVITY_SERVICE_UNAVAILABLE", "Current quest source validity is unavailable.");
            if (request.LinkedContributionIds is not null && request.LinkedContributionIds.Any(x => !proof.Ids.Contains(x)))
                return ClubReportHub.Shared.Data.ApiOperations.Error(400, "INVALID_QUEST_PROOF", "Every proof must be an eligible verified contribution of the author.");
            if (proof.Ids.Count < snapshot.Criteria.MinimumVerifiedContributions || proof.Points < snapshot.Criteria.MinimumAllocatedPoints)
                return ClubReportHub.Shared.Data.ApiOperations.Error(409, "QUEST_PROOF_REQUIRED", "Verified contributions do not meet the frozen criteria.");
            if (!await dbContext.QuestCompletionRecords.AnyAsync(x => x.ParticipantId == participant.Id
                && x.CriteriaVersion == snapshot.CriteriaVersion && x.LedgerRevision == proof.Revision, cancellationToken))
                dbContext.QuestCompletionRecords.Add(new QuestCompletionRecord
                {
                    ParticipantId = participant.Id,
                    CriteriaVersion = snapshot.CriteriaVersion,
                    LedgerRevision = proof.Revision,
                    RecordedAt = DateTimeOffset.UtcNow,
                    SnapshotJson = AdminService.Services.VerifiedExperienceService.Json(new { snapshot, linkedContributionIds = proof.Ids, allocatedPoints = proof.Points })
                });
            participant.Status = "Completed"; participant.CompletedAtUtc ??= DateTimeOffset.UtcNow;
            participant.CompletedLedgerRevision = proof.Revision; participant.LinkedContributionIdsJson = AdminService.Services.VerifiedExperienceService.Json(proof.Ids);
            participant.VerifiedByName = "VERIFIED_CONTRIBUTION_RULE"; participant.Version = Guid.NewGuid();
            if (request.Note is not null) participant.Note = request.Note.Trim();
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(await AdminService.Services.StudentQuestService.ResponseAsync(participant, dbContext, sources, cancellationToken));
        }, cancellationToken, required: false);
    }

    private static async Task<IResult> GetMyQuestsAsync(int? page, int? pageSize, string? semester,
        AdminDbContext dbContext, ICurrentActor currentActor, StudentExperienceSourceClient sources, CancellationToken cancellationToken)
    {
        if (page is < 1 || pageSize is < 1 or > 100) return Results.BadRequest();
        var uid = currentActor.UserId ?? 0;
        if (uid <= 0) return Results.Forbid();
        var query = dbContext.QuestParticipants.AsNoTracking().Where(x => x.StudentUserId == uid);
        if (!string.IsNullOrWhiteSpace(semester)) query = query.Where(x => x.Quest!.SemesterCode == semester);
        var current = page ?? 1; var size = pageSize ?? 20; var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.JoinedAtUtc).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(current - 1) * size, int.MaxValue)).Take(size).ToListAsync(cancellationToken);
        var items = new List<QuestParticipantResponse>();
        foreach (var row in rows) items.Add(await AdminService.Services.StudentQuestService.ResponseAsync(row, dbContext, sources, cancellationToken));
        return page.HasValue || pageSize.HasValue ? Results.Ok(new
        {
            items,
            page = current,
            pageSize = size,
            totalItems = total,
            totalPages = (int)Math.Ceiling((decimal)total / size)
        }) : Results.Ok(items);
    }

    private static async Task EnsureDefaultQuestsAsync(AdminDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await dbContext.Quests.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            var seeded = DefaultQuests.Select(def => new Quest
            {
                Title = def.Title,
                Description = def.Description,
                Category = def.Category,
                Kind = def.Kind,
                Scope = def.Scope,
                RewardXp = def.RewardXp,
                TargetParticipants = def.Target,
                SemesterCode = "FALL2026",
                CampusCode = "GLOBAL",
                Icon = def.Icon,
                DeadlineUtc = now.AddMonths(3),
                Status = def.Status,
                CreatedByName = "Cán bộ CTSV",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }).ToList();

            dbContext.Quests.AddRange(seeded);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
